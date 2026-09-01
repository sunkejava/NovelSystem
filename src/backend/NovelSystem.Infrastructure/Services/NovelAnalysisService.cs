using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Application.Contracts;
using NovelSystem.Application.Models;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Persistence;
using NovelSystem.Infrastructure.Jobs;

namespace NovelSystem.Infrastructure.Services;

/// <summary>
/// 长小说人物/脚本解析服务。
/// 采用紧凑 JSON、批量数据库写入、Prompt Cache 和显式进度持久化，
/// 尽量降低模型输出 token 与 SQLite 往返开销。
/// </summary>
public sealed class NovelAnalysisService(AppDbContext db, IAiChatClient aiClient) : INovelAnalysisService
{
    private const string AnalysisSystemPrompt =
        "你是中文小说人物与TTS脚本提取器。只输出JSON，不思考、不解释、不使用Markdown。";

    // 使用数组而不是每条记录重复 JSON 属性名，大幅减少长文本解析时的输出 token。
    private const string AnalysisInstruction =
        """
        按原文顺序提取。
        c=人物数组，每项格式：[姓名,性别,性格,简介]；旁白不放入c。
        s=TTS脚本数组，每项格式：[说话人,原文朗读文本,情绪]；叙述内容说话人固定为“旁白”。
        必须覆盖需要朗读的正文，不改写、不总结、不重复。
        同一说话人连续内容尽量合并为一条，目标80~250字，最长不超过400字；不要按单句机械拆分。
        情绪无明显特征时填空字符串。
        仅输出：{"c":[["","","",""]],"s":[["","",""]]}

        原文：
        """;

    public async Task AnalyzeAsync(long novelId, long jobId, CancellationToken cancellationToken = default)
    {
        var novel = await db.Novels.FindAsync([novelId], cancellationToken)
                    ?? throw new InvalidOperationException("小说不存在。");
        var job = await db.Jobs.FindAsync([jobId], cancellationToken)
                  ?? throw new InvalidOperationException("任务不存在。");

        var chunkText = await db.Settings
            .Where(x => x.Key == "AiChunkSize")
            .Select(x => x.Value)
            .FirstOrDefaultAsync(cancellationToken) ?? "8000";

        var chunkSize = int.TryParse(chunkText, out var configured)
            ? Math.Clamp(configured, 1000, 100000)
            : 8000;

        novel.Status = NovelStatus.Analyzing;
        await db.SaveChangesAsync(cancellationToken);

        var chunks = SplitByParagraph(novel.Content, chunkSize).ToList();

        // 断点必须直接从数据库读取，而不是依赖当前 DbContext 中可能陈旧的跟踪实体。
        // Checkpoint 的语义：已经完整解析并成功入库的“顶层小说分块数量”。
        // 因此失败在第 N 块时，数据库保存 N-1；重试 startIndex=N-1，正好从异常块重新开始。
        var persistedCheckpoint = await db.Jobs.AsNoTracking()
            .Where(x => x.Id == jobId)
            .Select(x => x.Checkpoint)
            .SingleAsync(cancellationToken);

        var startIndex = Math.Clamp(persistedCheckpoint, 0, chunks.Count);
        var initialProgress = chunks.Count == 0
            ? 0
            : (int)Math.Round(startIndex * 100d / chunks.Count);

        await db.Jobs
            .Where(x => x.Id == jobId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.TotalSteps, chunks.Count)
                .SetProperty(x => x.Progress, initialProgress),
                cancellationToken);

        // 同步本地对象仅用于 ETA 计算，不负责断点持久化。
        job.Checkpoint = startIndex;
        job.TotalSteps = chunks.Count;
        job.Progress = initialProgress;

        var scriptOrder = await db.ScriptLines
            .Where(x => x.NovelId == novelId)
            .MaxAsync(x => (int?)x.Order, cancellationToken)
            ?? 0;

        var existingCharacters = await db.Characters
            .Where(x => x.NovelId == novelId)
            .ToListAsync(cancellationToken);

        var characterMap = existingCharacters
            .GroupBy(x => NormalizeName(x.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        for (var index = startIndex; index < chunks.Count; index++)
        {
            // 直接从数据库读取任务状态，避免长时间 LLM 调用期间使用陈旧跟踪值。
            var currentStatus = await db.Jobs.AsNoTracking()
                .Where(x => x.Id == jobId)
                .Select(x => x.Status)
                .SingleAsync(cancellationToken);

            if (currentStatus == "Stopping")
                throw new OperationCanceledException("任务已由用户停止。");

            var parsed = await AnalyzeChunkWithFallbackAsync(
                chunks[index],
                novelId,
                jobId,
                index + 1,
                chunks.Count,
                cancellationToken);

            var newCharacters = new List<Character>();
            foreach (var item in parsed.Characters)
            {
                var normalizedName = NormalizeName(item.Name);
                if (string.IsNullOrWhiteSpace(normalizedName) ||
                    normalizedName.Equals("旁白", StringComparison.OrdinalIgnoreCase) ||
                    characterMap.ContainsKey(normalizedName))
                    continue;

                var character = new Character
                {
                    NovelId = novelId,
                    Name = item.Name.Trim(),
                    Gender = item.Gender,
                    Personality = item.Personality,
                    Description = item.Description
                };

                characterMap[normalizedName] = character;
                newCharacters.Add(character);
            }

            if (newCharacters.Count > 0)
            {
                db.Characters.AddRange(newCharacters);
                await db.SaveChangesAsync(cancellationToken);
            }

            var scriptEntities = new List<ScriptLine>(parsed.Scripts.Count);
            foreach (var item in parsed.Scripts)
            {
                if (string.IsNullOrWhiteSpace(item.Text))
                    continue;

                var speaker = string.IsNullOrWhiteSpace(item.Speaker) ? "旁白" : item.Speaker.Trim();
                characterMap.TryGetValue(NormalizeName(speaker), out var character);

                scriptEntities.Add(new ScriptLine
                {
                    NovelId = novelId,
                    CharacterId = character?.Id,
                    Order = ++scriptOrder,
                    Speaker = speaker,
                    Text = item.Text.Trim(),
                    Emotion = item.Emotion?.Trim()
                });
            }

            if (scriptEntities.Count > 0)
                db.ScriptLines.AddRange(scriptEntities);

            // 先保存本块人物/脚本，再用独立 SQL 更新任务进度。
            await db.SaveChangesAsync(cancellationToken);

            // 只有“当前顶层块完整解析 + 所有实体成功写库”之后，才推进断点。
            // AnalyzeChunkWithFallbackAsync 内部即使拆成多个子块，也不会提前改变顶层 checkpoint。
            job.Checkpoint = index + 1;
            job.Progress = (int)Math.Round(job.Checkpoint * 100d / Math.Max(chunks.Count, 1));
            JobTimingCalculator.Refresh(job);

            var checkpoint = job.Checkpoint;
            var progress = job.Progress;
            var totalSteps = job.TotalSteps;
            var elapsed = job.ElapsedMilliseconds;
            var average = job.AverageStepMilliseconds;
            var eta = job.EstimatedCompletionAt;

            await db.Jobs
                .Where(x => x.Id == jobId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Checkpoint, checkpoint)
                    .SetProperty(x => x.Progress, progress)
                    .SetProperty(x => x.TotalSteps, totalSteps)
                    .SetProperty(x => x.ElapsedMilliseconds, elapsed)
                    .SetProperty(x => x.AverageStepMilliseconds, average)
                    .SetProperty(x => x.EstimatedCompletionAt, eta),
                    cancellationToken);

            // 立即从数据库重新加载跟踪实体，确保后续异常处理看到的是最新断点。
            await db.Entry(job).ReloadAsync(cancellationToken);
        }

        novel.Status = NovelStatus.Analyzed;
        job.Progress = 100;
        job.Checkpoint = chunks.Count;
        job.TotalSteps = chunks.Count;
        JobTimingCalculator.Refresh(job);

        await db.Jobs
            .Where(x => x.Id == jobId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Checkpoint, job.Checkpoint)
                .SetProperty(x => x.Progress, 100)
                .SetProperty(x => x.TotalSteps, job.TotalSteps)
                .SetProperty(x => x.ElapsedMilliseconds, job.ElapsedMilliseconds)
                .SetProperty(x => x.AverageStepMilliseconds, job.AverageStepMilliseconds)
                .SetProperty(x => x.EstimatedCompletionAt, job.EstimatedCompletionAt),
                cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 调用 LLM 解析单个小说块。
    /// 当模型因为 max_tokens、上下文长度或偶发格式问题返回未闭合 JSON 时，
    /// 自动把当前块继续二分后分别解析并合并结果，避免整个长任务失败。
    /// </summary>
    private async Task<CompactAnalysisResult> AnalyzeChunkWithFallbackAsync(
        string chunk,
        long novelId,
        long jobId,
        int chunkIndex,
        int chunkTotal,
        CancellationToken cancellationToken,
        int depth = 0)
    {
        string? raw = null;

        try
        {
            raw = await aiClient.ChatJsonTrackedAsync(
                AnalysisSystemPrompt,
                AnalysisInstruction + chunk,
                new AiCallContext(
                    novelId,
                    jobId,
                    depth == 0 ? "AnalyzeNovelChunk" : "AnalyzeNovelChunkRetry",
                    chunkIndex,
                    chunkTotal),
                cancellationToken);

            try
            {
                return ParseCompactResult(raw);
            }
            catch (JsonException parseEx)
            {
                // 第一层本地容错：根据 JsonException 的行号/UTF-8 字节位置定位异常附近字符，
                // 仅修复 JSON 字符串内部可明确判断的未转义双引号、反斜杠、换行/制表符和控制字符。
                // 这一步不调用 LLM，成本最低，也不会因为重新生成而改变原脚本内容。
                if (TryRepairJsonEscapes(raw, parseEx, out var locallyRepaired, out var repairSummary))
                {
                    try
                    {
                        var repairedResult = ParseCompactResult(locallyRepaired);
                        var diagnostic = await SaveAnalysisErrorAsync(
                            novelId,
                            jobId,
                            chunkIndex,
                            chunkTotal,
                            depth,
                            "LocalEscapeRepair",
                            chunk,
                            raw,
                            new InvalidOperationException(parseEx.Message + Environment.NewLine + repairSummary, parseEx),
                            cancellationToken);
                        diagnostic.Recovered = true;
                        await db.SaveChangesAsync(cancellationToken);
                        return repairedResult;
                    }
                    catch (JsonException)
                    {
                        // 本地最小修复不足时继续进入现有 LLM JSON 修复 / 强约束 / 语义拆分流程。
                    }
                }

                throw;
            }
        }
        catch (JsonException ex)
        {
            var diagnostic = await SaveAnalysisErrorAsync(
                novelId,
                jobId,
                chunkIndex,
                chunkTotal,
                depth,
                IsLikelyTruncatedJson(ex, raw) ? "TruncatedJson" : "MalformedJson",
                chunk,
                raw,
                ex,
                cancellationToken);

            // 未闭合对象/数组通常说明输出被 max_tokens 或上下文上限截断。
            // 这类问题通过继续拆小当前片段最有效。
            if (IsLikelyTruncatedJson(ex, raw) && chunk.Length > 700 && depth < 6)
            {
                var splitResult = await SplitAndAnalyzeAsync(
                    chunk,
                    novelId,
                    jobId,
                    chunkIndex,
                    chunkTotal,
                    cancellationToken,
                    depth,
                    ex);
                diagnostic.Recovered = true;
                await db.SaveChangesAsync(cancellationToken);
                return splitResult;
            }

            // 冒号、逗号、引号位置错误属于“模型生成了错误 JSON”，
            // 对这种情况继续二分通常没有意义。先让模型只修 JSON，不重新分析原文。
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    var repaired = await aiClient.ChatTrackedAsync(
                        "你是JSON修复器。只输出修复后的合法JSON，不解释、不使用Markdown，不增加或删除业务内容。",
                        """
                        修复下面的小说解析结果，使其成为严格合法的 JSON。
                        顶层必须保持 {"c":[...],"s":[...]}。
                        c 的每项必须是 [姓名,性别,性格,简介]。
                        s 的每项必须是 [说话人,原文朗读文本,情绪]。
                        仅修复引号、反斜杠、逗号、冒号、括号等 JSON 语法，不重新创作内容。

                        待修复内容：
                        """ + raw,
                        new AiCallContext(
                            novelId,
                            jobId,
                            "AnalyzeNovelJsonRepair",
                            chunkIndex,
                            chunkTotal),
                        cancellationToken);

                    var repairedResult = ParseCompactResultWithEscapeRepair(repaired);
                    diagnostic.Recovered = true;
                    await db.SaveChangesAsync(cancellationToken);
                    return repairedResult;
                }
                catch (JsonException)
                {
                    // 修复输出仍非法，继续走强约束重试。
                }
            }

            // 第二层兜底：只对当前失败片段临时启用 response_format=json_object，
            // 正常解析仍保持快速模式，不影响整体吞吐。
            try
            {
                var strictRaw = await aiClient.ChatJsonStrictTrackedAsync(
                    AnalysisSystemPrompt,
                    AnalysisInstruction + chunk,
                    new AiCallContext(
                        novelId,
                        jobId,
                        "AnalyzeNovelChunkStrict",
                        chunkIndex,
                        chunkTotal),
                    cancellationToken);

                var strictResult = ParseCompactResultWithEscapeRepair(strictRaw);
                diagnostic.Recovered = true;
                await db.SaveChangesAsync(cancellationToken);
                return strictResult;
            }
            catch (Exception strictEx) when (chunk.Length > 700 && depth < 6)
            {
                // 某些 llama.cpp / 模型组合的强约束解析也可能失败。
                // 最后才缩小片段，避免单个坏 JSON 阻塞整本小说。
                var splitResult = await SplitAndAnalyzeAsync(
                    chunk,
                    novelId,
                    jobId,
                    chunkIndex,
                    chunkTotal,
                    cancellationToken,
                    depth,
                    strictEx);
                diagnostic.Recovered = true;
                await db.SaveChangesAsync(cancellationToken);
                return splitResult;
            }
            catch (Exception strictEx)
            {
                throw new InvalidOperationException(
                    $"LLM 连续返回非法 JSON。片段长度={chunk.Length}，已尝试普通解析、JSON修复和强约束重试。建议检查模型/chat template，或临时开启设置中的“JSON 约束解码”。",
                    strictEx);
            }
        }
    }

    private async Task<CompactAnalysisResult> SplitAndAnalyzeAsync(
        string chunk,
        long novelId,
        long jobId,
        int chunkIndex,
        int chunkTotal,
        CancellationToken cancellationToken,
        int depth,
        Exception sourceException)
    {
        var parts = SplitForRetry(chunk).ToList();
        if (parts.Count <= 1)
            throw new InvalidOperationException(
                $"LLM 返回的 JSON 无法恢复，当前片段无法继续安全拆分。片段长度={chunk.Length}。",
                sourceException);

        var merged = new CompactAnalysisResult();

        foreach (var part in parts)
        {
            var child = await AnalyzeChunkWithFallbackAsync(
                part,
                novelId,
                jobId,
                chunkIndex,
                chunkTotal,
                cancellationToken,
                depth + 1);

            merged.Characters.AddRange(child.Characters);
            merged.Scripts.AddRange(child.Scripts);
        }

        return merged;
    }

    private static bool IsLikelyTruncatedJson(JsonException exception, string? raw)
    {
        var message = exception.Message;
        if (message.Contains("open JSON object or array", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Expected depth to be zero", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("end of data", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("incomplete", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var text = raw.TrimEnd();
        // 明显没有闭合顶层对象，通常就是生成被截断。
        return text.StartsWith('{') && !text.EndsWith('}');
    }

    /// <summary>
    /// JSON 截断重试时优先在自然段附近二分，尽量避免把一句话/对白切成两半。
    /// </summary>
    private static IEnumerable<string> SplitForRetry(string chunk)
    {
        if (string.IsNullOrWhiteSpace(chunk))
            yield break;

        var midpoint = chunk.Length / 2;
        var splitIndex = FindSemanticBoundary(chunk, midpoint, Math.Max(120, chunk.Length / 4));

        var left = chunk[..splitIndex].Trim();
        var right = chunk[splitIndex..].Trim();

        if (!string.IsNullOrWhiteSpace(left))
            yield return left;
        if (!string.IsNullOrWhiteSpace(right))
            yield return right;
    }

    /// <summary>
    /// 对 LLM 返回的 JSON 做一次“只修转义、不改业务内容”的本地容错解析。
    /// </summary>
    private static CompactAnalysisResult ParseCompactResultWithEscapeRepair(string raw)
    {
        try
        {
            return ParseCompactResult(raw);
        }
        catch (JsonException ex) when (TryRepairJsonEscapes(raw, ex, out var repaired, out _))
        {
            return ParseCompactResult(repaired);
        }
    }

    /// <summary>
    /// 根据 System.Text.Json 返回的 LineNumber / BytePositionInLine 定位异常，
    /// 并对整个 JSON 做保守的字符串状态扫描。
    ///
    /// 只处理以下“模型最常见”的非法 JSON：
    /// 1. JSON 字符串内部出现未转义的英文双引号，例如："他说："你好""；
    /// 2. 字符串内部出现非法反斜杠，例如 Windows 路径或 \q；
    /// 3. 字符串内部出现真实换行、Tab、回车或其他 U+0000~U+001F 控制字符。
    ///
    /// 不会修改字符串外的结构字符，也不会尝试补括号/逗号，因此不会掩盖真正的结构化输出错误。
    /// </summary>
    private static bool TryRepairJsonEscapes(
        string raw,
        JsonException exception,
        out string repaired,
        out string summary)
    {
        repaired = raw;
        summary = string.Empty;

        var json = NormalizeJson(raw);
        if (string.IsNullOrWhiteSpace(json))
            return false;

        var errorCharIndex = GetCharIndexFromJsonException(json, exception);
        var builder = new System.Text.StringBuilder(json.Length + 32);
        var repairs = new List<string>();
        var inString = false;
        var changed = false;

        for (var i = 0; i < json.Length; i++)
        {
            var ch = json[i];

            if (!inString)
            {
                builder.Append(ch);
                if (ch == '"')
                    inString = true;
                continue;
            }

            if (ch == '\\')
            {
                // JSON 合法转义：\" \\ \/ \b \f \n \r \t \uXXXX
                if (i + 1 < json.Length)
                {
                    var next = json[i + 1];
                    if (next is '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't')
                    {
                        builder.Append(ch);
                        builder.Append(next);
                        i++;
                        continue;
                    }

                    if (next == 'u' && HasFourHexDigits(json, i + 2))
                    {
                        builder.Append(ch);
                        builder.Append('u');
                        builder.Append(json, i + 2, 4);
                        i += 5;
                        continue;
                    }

                    // 非法 \x / Windows 路径等：把当前反斜杠本身转义为 \\，
                    // 后一个字符保持原样，避免修改业务文本。
                    builder.Append("\\\\");
                    changed = true;
                    repairs.Add($"index={i}: 非法反斜杠转义 -> \\\\");
                    continue;
                }

                // 字符串末尾孤立反斜杠。
                builder.Append("\\\\");
                changed = true;
                repairs.Add($"index={i}: 末尾孤立反斜杠 -> \\\\");
                continue;
            }

            if (ch == '"')
            {
                // 字符串真正结束后，下一个非空白字符必须是 JSON 结构字符。
                // 如果后面仍是普通正文字符，则这个双引号大概率属于小说对白，模型漏写了反斜杠。
                if (!LooksLikeStringTerminator(json, i + 1))
                {
                    builder.Append("\\\"");
                    changed = true;
                    repairs.Add($"index={i}: 字符串内部未转义双引号 -> 已补反斜杠转义");
                    continue;
                }

                builder.Append(ch);
                inString = false;
                continue;
            }

            if (ch == '\n')
            {
                builder.Append("\\n");
                changed = true;
                repairs.Add($"index={i}: 字符串内部换行 -> \\n");
                continue;
            }

            if (ch == '\r')
            {
                builder.Append("\\r");
                changed = true;
                repairs.Add($"index={i}: 字符串内部回车 -> \\r");
                continue;
            }

            if (ch == '\t')
            {
                builder.Append("\\t");
                changed = true;
                repairs.Add($"index={i}: 字符串内部 Tab -> \\t");
                continue;
            }

            if (ch < 0x20)
            {
                builder.Append("\\u");
                builder.Append(((int)ch).ToString("x4"));
                changed = true;
                repairs.Add($"index={i}: 控制字符 U+{(int)ch:X4} -> Unicode 转义");
                continue;
            }

            builder.Append(ch);
        }

        if (!changed)
        {
            summary = $"未发现可安全自动转义的字符。异常字符位置≈{errorCharIndex}。";
            return false;
        }

        repaired = builder.ToString();

        var nearby = DescribeNearbyCharacters(json, errorCharIndex);
        var nearestRepairs = repairs
            .OrderBy(x => DistanceFromRepairIndex(x, errorCharIndex))
            .Take(8);

        summary =
            $"本地 JSON 转义修复：原异常 Line={exception.LineNumber}, BytePosition={exception.BytePositionInLine}, " +
            $"映射字符位置≈{errorCharIndex}。异常附近={nearby}。修复 {repairs.Count} 处：" +
            string.Join("；", nearestRepairs);

        return true;
    }

    private static int GetCharIndexFromJsonException(string json, JsonException exception)
    {
        var targetLine = Math.Max(0L, exception.LineNumber ?? 0L);
        var bytePosition = Math.Max(0L, exception.BytePositionInLine ?? 0L);

        var lineStart = 0;
        long currentLine = 0;
        for (var i = 0; i < json.Length && currentLine < targetLine; i++)
        {
            if (json[i] != '\n') continue;
            currentLine++;
            lineStart = i + 1;
        }

        var lineEnd = json.IndexOf('\n', lineStart);
        if (lineEnd < 0) lineEnd = json.Length;
        var line = json[lineStart..lineEnd];

        // BytePositionInLine 是 UTF-8 字节偏移，中文不能直接当 char index 使用。
        var utf8 = System.Text.Encoding.UTF8;
        var consumedBytes = 0L;
        var charOffset = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var runeBytes = utf8.GetByteCount(rune.ToString());
            if (consumedBytes + runeBytes > bytePosition)
                break;
            consumedBytes += runeBytes;
            charOffset += rune.Utf16SequenceLength;
        }

        return Math.Clamp(lineStart + charOffset, 0, Math.Max(0, json.Length - 1));
    }

    private static bool HasFourHexDigits(string value, int start)
    {
        if (start + 4 > value.Length) return false;
        for (var i = start; i < start + 4; i++)
            if (!Uri.IsHexDigit(value[i]))
                return false;
        return true;
    }

    private static int NextNonWhitespaceIndex(string value, int start)
    {
        for (var i = start; i < value.Length; i++)
            if (!char.IsWhiteSpace(value[i]))
                return i;
        return -1;
    }

    /// <summary>
    /// 判断当前双引号是否看起来真的是 JSON 字符串结束符。
    /// 不只检查紧随其后的结构字符，还检查结构字符之后是否仍符合 JSON 语法，
    /// 避免正文中的："你好",然后…… 被误认为数组元素已经结束。
    /// </summary>
    private static bool LooksLikeStringTerminator(string value, int start)
    {
        var nextIndex = NextNonWhitespaceIndex(value, start);
        if (nextIndex < 0)
            return true;

        var next = value[nextIndex];
        if (next is ']' or '}')
            return true;

        if (next == ':')
        {
            // 属性名结束后冒号后面必须能开始一个合法 JSON 值。
            var valueIndex = NextNonWhitespaceIndex(value, nextIndex + 1);
            if (valueIndex < 0) return false;
            return IsJsonValueStart(value[valueIndex]);
        }

        if (next == ',')
        {
            // 数组/对象下一个成员通常以字符串、对象、数组、数字、布尔/null 开始，
            // 或直接闭合。普通中文/英文字母正文不应被当成结构分隔。
            var followingIndex = NextNonWhitespaceIndex(value, nextIndex + 1);
            if (followingIndex < 0) return false;
            var following = value[followingIndex];
            return following is '"' or '[' or '{' or ']' or '}' or '-' ||
                   char.IsDigit(following) ||
                   following is 't' or 'f' or 'n';
        }

        return false;
    }

    private static bool IsJsonValueStart(char ch)
        => ch is '"' or '[' or '{' or '-' ||
           char.IsDigit(ch) ||
           ch is 't' or 'f' or 'n';

    private static string DescribeNearbyCharacters(string value, int index)
    {
        if (value.Length == 0) return "<empty>";
        var start = Math.Max(0, index - 24);
        var length = Math.Min(value.Length - start, 56);
        return JsonSerializer.Serialize(value.Substring(start, length));
    }

    private static int DistanceFromRepairIndex(string repair, int target)
    {
        const string prefix = "index=";
        var start = repair.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0) return int.MaxValue;
        start += prefix.Length;
        var end = repair.IndexOf(':', start);
        if (end < 0 || !int.TryParse(repair[start..end], out var index))
            return int.MaxValue;
        return Math.Abs(index - target);
    }

    private static CompactAnalysisResult ParseCompactResult(string raw)
    {
        var json = NormalizeJson(raw);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var result = new CompactAnalysisResult();

        if (root.TryGetProperty("c", out var characters) && characters.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in characters.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array) continue;
                var values = row.EnumerateArray().Select(ReadString).ToList();
                if (values.Count == 0 || string.IsNullOrWhiteSpace(values[0])) continue;

                result.Characters.Add(new CompactCharacter(
                    values.ElementAtOrDefault(0) ?? string.Empty,
                    values.ElementAtOrDefault(1),
                    values.ElementAtOrDefault(2),
                    values.ElementAtOrDefault(3)));
            }
        }

        if (root.TryGetProperty("s", out var scripts) && scripts.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in scripts.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array) continue;
                var values = row.EnumerateArray().Select(ReadString).ToList();
                if (values.Count < 2 || string.IsNullOrWhiteSpace(values[1])) continue;

                result.Scripts.Add(new CompactScript(
                    values.ElementAtOrDefault(0) ?? "旁白",
                    values.ElementAtOrDefault(1) ?? string.Empty,
                    values.ElementAtOrDefault(2)));
            }
        }

        return result;
    }

    private static string? ReadString(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null => null,
            _ => element.ToString()
        };

    private static IEnumerable<string> SplitByParagraph(string text, int chunkSize)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var paragraphs = normalized.Split('\n');
        var buffer = new System.Text.StringBuilder(chunkSize + 512);

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length > chunkSize)
            {
                if (buffer.Length > 0)
                {
                    yield return buffer.ToString();
                    buffer.Clear();
                }

                foreach (var part in SplitLongTextAtSemanticBoundaries(paragraph, chunkSize))
                    yield return part;
                continue;
            }

            if (buffer.Length > 0 && buffer.Length + paragraph.Length + 1 > chunkSize)
            {
                yield return buffer.ToString();
                buffer.Clear();
            }

            if (buffer.Length > 0)
                buffer.Append('\n');

            buffer.Append(paragraph);
        }

        if (buffer.Length > 0)
            yield return buffer.ToString();
    }

    /// <summary>
    /// 长段落按“软长度”切分。到达目标长度后优先向后寻找完整句子边界，
    /// 最多允许额外延长约 35%，避免把人物对白或句子从中间截断。
    /// </summary>
    private static IEnumerable<string> SplitLongTextAtSemanticBoundaries(string text, int targetSize)
    {
        var offset = 0;
        while (offset < text.Length)
        {
            var remaining = text.Length - offset;
            if (remaining <= targetSize)
            {
                yield return text[offset..];
                yield break;
            }

            var target = offset + targetSize;
            var searchRadius = Math.Max(200, (int)(targetSize * 0.35));
            var boundary = FindSemanticBoundary(text, target, searchRadius);
            if (boundary <= offset)
                boundary = Math.Min(offset + targetSize, text.Length);

            yield return text[offset..boundary].Trim();
            offset = boundary;
        }
    }

    private static int FindSemanticBoundary(string text, int target, int radius)
    {
        if (text.Length == 0) return 0;
        target = Math.Clamp(target, 1, text.Length - 1);
        var sentenceMarks = new[] { '\n', '。', '！', '？', '；', '!', '?', ';', '…' };

        // 优先向后延长到完整语义边界。
        var rightLimit = Math.Min(text.Length - 1, target + radius);
        for (var i = target; i <= rightLimit; i++)
            if (sentenceMarks.Contains(text[i]))
                return i + 1;

        // 向后找不到时再向前找，避免无限延长。
        var leftLimit = Math.Max(1, target - radius);
        for (var i = target - 1; i >= leftLimit; i--)
            if (sentenceMarks.Contains(text[i]))
                return i + 1;

        return target;
    }

    private async Task<AiAnalysisError> SaveAnalysisErrorAsync(
        long novelId,
        long jobId,
        int chunkIndex,
        int chunkTotal,
        int retryDepth,
        string stage,
        string sourceText,
        string? rawResponse,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var entity = new AiAnalysisError
        {
            NovelId = novelId,
            JobId = jobId,
            ChunkIndex = chunkIndex,
            ChunkTotal = chunkTotal,
            RetryDepth = retryDepth,
            Stage = stage,
            SourceText = sourceText,
            RawResponse = rawResponse,
            Error = exception.ToString(),
            Recovered = false,
            CreatedAt = DateTime.UtcNow
        };
        db.AiAnalysisErrors.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    private static string NormalizeName(string? name)
        => string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();

    private static string NormalizeJson(string value)
    {
        var text = value.Trim();
        var first = text.IndexOf('{');
        var last = text.LastIndexOf('}');
        return first >= 0 && last >= first ? text[first..(last + 1)] : text;
    }

    private sealed class CompactAnalysisResult
    {
        public List<CompactCharacter> Characters { get; } = [];
        public List<CompactScript> Scripts { get; } = [];
    }

    private sealed record CompactCharacter(string Name, string? Gender, string? Personality, string? Description);
    private sealed record CompactScript(string Speaker, string Text, string? Emotion);
}