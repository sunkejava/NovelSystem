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
/// 优化重点：LLM 分块调用 + Prompt Cache + 人物一次加载 + 脚本批量映射/写入，
/// 避免原实现每个人物/每条脚本一次数据库查询造成的 N+1 性能问题。
/// </summary>
public sealed class NovelAnalysisService(AppDbContext db, IAiChatClient aiClient) : INovelAnalysisService
{
    private const string AnalysisSystemPrompt =
        "你是专业中文小说结构分析器。任务是提取人物与可用于多角色TTS的脚本。必须只输出合法JSON，不要解释。";

    private const string AnalysisInstruction =
        """
        按原文顺序提取人物与TTS脚本。
        要求：
        1. characters 只列本片段真实出现的重要人物；不要把“旁白”当人物。
        2. scripts 必须覆盖需要朗读的正文；人物对白 speaker=人物名，叙述内容 speaker=旁白。
        3. text 保留原文语义，不增加解释，不重复。
        4. emotion 使用简短中文情绪词，没有明显情绪可留空。
        输出JSON：
        {"characters":[{"name":"","gender":"","personality":"","description":""}],"scripts":[{"speaker":"人物名或旁白","text":"","emotion":""}]}

        小说片段：
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
            .FirstOrDefaultAsync(cancellationToken) ?? "12000";

        var chunkSize = int.TryParse(chunkText, out var configured)
            ? Math.Clamp(configured, 1000, 100000)
            : 12000;

        novel.Status = NovelStatus.Analyzing;
        await db.SaveChangesAsync(cancellationToken);

        // 尽量按段落边界切块，避免硬切在一句对白中间，减少模型重复理解上下文。
        var chunks = SplitByParagraph(novel.Content, chunkSize).ToList();
        job.TotalSteps = chunks.Count;

        var startIndex = Math.Clamp(job.Checkpoint, 0, chunks.Count);

        var scriptOrder = await db.ScriptLines
            .Where(x => x.NovelId == novelId)
            .MaxAsync(x => (int?)x.Order, cancellationToken)
            ?? 0;

        // 一次性加载现有人物，后续所有分块在内存字典中判断/映射。
        var existingCharacters = await db.Characters
            .Where(x => x.NovelId == novelId)
            .ToListAsync(cancellationToken);

        var characterMap = existingCharacters
            .GroupBy(x => NormalizeName(x.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        for (var index = startIndex; index < chunks.Count; index++)
        {
            await db.Entry(job).ReloadAsync(cancellationToken);
            if (job.Status == "Stopping")
                throw new OperationCanceledException("任务已由用户停止。");

            var raw = await aiClient.ChatJsonAsync(
                AnalysisSystemPrompt,
                AnalysisInstruction + chunks[index],
                cancellationToken);

            var result = JsonSerializer.Deserialize<AiAnalysisResult>(
                NormalizeJson(raw),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new AiAnalysisResult();

            // 先在内存里计算本块新增人物，单次 AddRange + SaveChanges。
            var newCharacters = new List<Character>();
            foreach (var item in result.Characters)
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
                // 这里保存一次是为了获得新增人物 Id，后面的 ScriptLine 可直接引用。
                await db.SaveChangesAsync(cancellationToken);
            }

            // 脚本完全在内存中映射 CharacterId，不再每条 FirstOrDefaultAsync。
            var scriptEntities = new List<ScriptLine>(result.Scripts.Count);
            foreach (var item in result.Scripts)
            {
                if (string.IsNullOrWhiteSpace(item.Text))
                    continue;

                var speaker = string.IsNullOrWhiteSpace(item.Speaker)
                    ? "旁白"
                    : item.Speaker.Trim();

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

            job.Checkpoint = index + 1;
            job.Progress = (int)Math.Round(job.Checkpoint * 100d / Math.Max(chunks.Count, 1));
            JobTimingCalculator.Refresh(job);

            // 每个分块最多两次 SaveChanges：一次新增人物取 Id，一次批量写脚本/任务进度。
            await db.SaveChangesAsync(cancellationToken);
        }

        novel.Status = NovelStatus.Analyzed;
        job.Progress = 100;
        job.Checkpoint = chunks.Count;
        JobTimingCalculator.Refresh(job);
        await db.SaveChangesAsync(cancellationToken);
    }

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
}