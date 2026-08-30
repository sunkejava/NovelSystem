using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Application.Contracts;
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
        var startIndex = Math.Clamp(job.Checkpoint, 0, chunks.Count);

        job.TotalSteps = chunks.Count;
        job.Progress = chunks.Count == 0
            ? 0
            : (int)Math.Round(startIndex * 100d / chunks.Count);
        JobTimingCalculator.Refresh(job);
        await db.SaveChangesAsync(cancellationToken);

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

            var raw = await aiClient.ChatJsonAsync(
                AnalysisSystemPrompt,
                AnalysisInstruction + chunks[index],
                cancellationToken);

            var parsed = ParseCompactResult(raw);

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

            // ExecuteUpdate 不更新已跟踪实体的 OriginalValues，这里同步标记避免后续 SaveChanges 覆盖数据库进度。
            db.Entry(job).State = EntityState.Unchanged;
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

                for (var offset = 0; offset < paragraph.Length; offset += chunkSize)
                    yield return paragraph.Substring(offset, Math.Min(chunkSize, paragraph.Length - offset));
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