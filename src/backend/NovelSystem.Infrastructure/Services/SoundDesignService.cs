using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Application.Contracts;
using NovelSystem.Application.Models;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Infrastructure.Services;

/// <summary>
/// AI 声音导演。按章节分析场景，并让模型只返回“脚本顺序号”，
/// 最终时间点始终由 ScriptLine.AudioStartMs/AudioEndMs 映射，避免模型直接编造时间轴。
/// </summary>
public static class SoundDesignService
{
    public static async Task<int> AnalyzeNovelAsync(
        AppDbContext db,
        IAiChatClient ai,
        long novelId,
        long? jobId,
        Func<int, int, Task>? progress,
        CancellationToken cancellationToken)
    {
        var chapters = await db.NovelChapters.AsNoTracking()
            .Where(x => x.NovelId == novelId)
            .OrderBy(x => x.ChapterOrder)
            .ToListAsync(cancellationToken);
        if (chapters.Count == 0)
            throw new InvalidOperationException("请先在专业制作中心建立章节结构。");

        var old = await db.SoundDesignCues.Where(x => x.NovelId == novelId && !x.Approved).ToListAsync(cancellationToken);
        if (old.Count > 0)
        {
            db.SoundDesignCues.RemoveRange(old);
            await db.SaveChangesAsync(cancellationToken);
        }

        var created = 0;
        for (var index = 0; index < chapters.Count; index++)
        {
            var chapter = chapters[index];
            var count = await AnalyzeChapterAsync(db, ai, novelId, chapter.Id, jobId, index + 1, chapters.Count, cancellationToken);
            created += count;
            if (progress is not null)
                await progress(index + 1, chapters.Count);
        }
        return created;
    }

    public static async Task<int> AnalyzeChapterAsync(
        AppDbContext db,
        IAiChatClient ai,
        long novelId,
        long chapterId,
        long? jobId,
        int? chunkIndex,
        int? chunkTotal,
        CancellationToken cancellationToken)
    {
        var chapter = await db.NovelChapters.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == chapterId && x.NovelId == novelId, cancellationToken)
            ?? throw new InvalidOperationException("章节不存在。");

        var scripts = await db.ScriptLines.AsNoTracking()
            .Where(x => x.NovelId == novelId && x.ChapterId == chapterId)
            .OrderBy(x => x.Order)
            .Select(x => new { x.Id, x.Order, x.Speaker, x.Text, x.Emotion, x.AudioStartMs, x.AudioEndMs })
            .ToListAsync(cancellationToken);
        if (scripts.Count == 0)
            return 0;

        var compact = scripts.Select(x => new
        {
            order = x.Order,
            speaker = x.Speaker,
            emotion = x.Emotion,
            text = x.Text.Length > 180 ? x.Text[..180] : x.Text
        });

        var raw = await ai.ChatJsonTrackedAsync(
            """
            你是专业中文有声书声音导演。你的职责是分析小说章节并规划背景音乐与环境/动作音效。
            所有文字字段必须使用简体中文。不要生成英文标题、英文说明或英文关键词。
            不要给每句对白都加音效，只在真正能增强叙事的场景设置 Cue，避免声音过密影响朗读。
            BGM 适合持续氛围；SFX 适合瞬时或短时事件。
            """,
            """
            请根据下面脚本生成声音设计方案，只返回 JSON：
            {
              "cues":[
                {
                  "type":"Bgm或Sfx",
                  "startOrder":脚本顺序号,
                  "endOrder":脚本顺序号,
                  "scene":"场景类型",
                  "mood":"情绪氛围",
                  "intensity":0到100,
                  "keywords":"中文素材搜索关键词，逗号分隔",
                  "description":"给声音制作人员的中文说明"
                }
              ]
            }

            规则：
            1. startOrder/endOrder 必须来自提供的脚本 order，不得虚构；
            2. SFX 通常 startOrder=endOrder；BGM 可以覆盖连续多条脚本；
            3. 同一场景不要重复创建高度相似的 BGM；
            4. 对纯对白且无需声音强化的区域可以不创建 Cue；
            5. 所有说明与关键词必须为中文。

            章节标题：
            """ + chapter.Title + "\n\n脚本：\n" + JsonSerializer.Serialize(compact),
            new AiCallContext(novelId, jobId, "SoundDesignChapter", chunkIndex, chunkTotal),
            cancellationToken);

        var suggestions = ParseCues(raw);
        var byOrder = scripts.ToDictionary(x => x.Order);
        var entities = new List<SoundDesignCue>();
        foreach (var cue in suggestions)
        {
            if (!byOrder.TryGetValue(cue.StartOrder, out var startLine))
                continue;
            var endOrder = Math.Max(cue.StartOrder, cue.EndOrder);
            var endLine = byOrder.TryGetValue(endOrder, out var foundEnd) ? foundEnd : startLine;
            if (!startLine.AudioStartMs.HasValue)
                continue;

            var startMs = startLine.AudioStartMs.Value;
            var endMs = endLine.AudioEndMs ?? endLine.AudioStartMs ?? startMs;
            if (endMs <= startMs)
                endMs = startMs + (string.Equals(cue.Type, "Bgm", StringComparison.OrdinalIgnoreCase) ? 30000 : 3000);

            entities.Add(new SoundDesignCue
            {
                NovelId = novelId,
                ChapterId = chapterId,
                ScriptLineId = startLine.Id,
                Type = string.Equals(cue.Type, "Bgm", StringComparison.OrdinalIgnoreCase) ? "Bgm" : "Sfx",
                Scene = Trim(cue.Scene, 100),
                Mood = Trim(cue.Mood, 100),
                Intensity = Math.Clamp(cue.Intensity, 0, 100),
                Keywords = Trim(cue.Keywords, 300),
                Description = Trim(cue.Description, 500),
                StartMs = startMs,
                EndMs = endMs,
                Approved = false,
                Applied = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (entities.Count > 0)
        {
            db.SoundDesignCues.AddRange(entities);
            await db.SaveChangesAsync(cancellationToken);
        }
        return entities.Count;
    }

    private static List<CueDto> ParseCues(string raw)
    {
        var json = raw.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = json.IndexOf('\n');
            var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
                json = json[(firstNewLine + 1)..lastFence].Trim();
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("cues", out var cues) || cues.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<CueDto>();
        foreach (var item in cues.EnumerateArray())
        {
            if (!item.TryGetProperty("startOrder", out var start) || !start.TryGetInt32(out var startOrder))
                continue;
            var endOrder = item.TryGetProperty("endOrder", out var end) && end.TryGetInt32(out var value) ? value : startOrder;
            result.Add(new CueDto(
                GetString(item, "type"), startOrder, endOrder,
                GetString(item, "scene"), GetString(item, "mood"),
                item.TryGetProperty("intensity", out var intensity) && intensity.TryGetInt32(out var iv) ? iv : 50,
                GetString(item, "keywords"), GetString(item, "description")));
        }
        return result;
    }

    private static string GetString(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static string Trim(string value, int max)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim()[..Math.Min(value.Trim().Length, max)];

    private sealed record CueDto(string Type, int StartOrder, int EndOrder, string Scene, string Mood, int Intensity, string Keywords, string Description);
}
