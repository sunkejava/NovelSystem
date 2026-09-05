using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Infrastructure.Services;

/// <summary>
/// 小说卷/章节识别与脚本原文定位服务。
/// 不依赖 LLM，保证章节结构和 Source Offset 可重复、可追溯。
/// </summary>
public static partial class NovelStructureService
{
    /// <summary>重建卷/章节结构，并给已有脚本回填 SourceStart/SourceEnd/ChapterId。</summary>
    public static async Task<int> RebuildAsync(AppDbContext db, long novelId, CancellationToken cancellationToken = default)
    {
        var novel = await db.Novels.FindAsync([novelId], cancellationToken)
                    ?? throw new InvalidOperationException("小说不存在。");

        var source = Normalize(novel.Content);
        var old = await db.NovelChapters.Where(x => x.NovelId == novelId).ToListAsync(cancellationToken);
        if (old.Count > 0)
            db.NovelChapters.RemoveRange(old);
        await db.SaveChangesAsync(cancellationToken);

        var chapters = DetectChapters(novelId, source);
        db.NovelChapters.AddRange(chapters);
        await db.SaveChangesAsync(cancellationToken);

        await BackfillScriptOffsetsAsync(db, novelId, source, chapters, cancellationToken);
        return chapters.Count;
    }

    /// <summary>解析前确保章节结构存在；已有章节则不重复扫描整本正文。</summary>
    public static async Task EnsureAsync(AppDbContext db, Novel novel, CancellationToken cancellationToken = default)
    {
        if (await db.NovelChapters.AsNoTracking().AnyAsync(x => x.NovelId == novel.Id, cancellationToken))
            return;

        var source = Normalize(novel.Content);
        var chapters = DetectChapters(novel.Id, source);
        db.NovelChapters.AddRange(chapters);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static List<NovelChapter> DetectChapters(long novelId, string source)
    {
        var matches = HeadingRegex().Matches(source)
            .Cast<Match>()
            .Where(x => x.Success)
            .OrderBy(x => x.Index)
            .ToList();

        // 没检测到章节标题时也创建一个“正文”章节，保证所有脚本都有可归属区间。
        if (matches.Count == 0)
        {
            return
            [
                new NovelChapter
                {
                    NovelId = novelId,
                    ChapterOrder = 1,
                    VolumeOrder = 1,
                    Title = "正文",
                    SourceStart = 0,
                    SourceEnd = source.Length,
                    CreatedAt = DateTime.UtcNow
                }
            ];
        }

        var result = new List<NovelChapter>(matches.Count);
        var volumeTitle = (string?)null;
        var volumeOrder = 1;
        var chapterOrder = 0;

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var title = match.Groups["title"].Value.Trim();
            var isVolume = VolumeRegex().IsMatch(title);
            if (isVolume)
            {
                volumeTitle = title;
                volumeOrder++;
                continue;
            }

            chapterOrder++;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : source.Length;
            // 如果下一条是卷标题，再找卷标题后的下一个章标题作为当前章结束点。
            var nextIndex = i + 1;
            while (nextIndex < matches.Count && VolumeRegex().IsMatch(matches[nextIndex].Groups["title"].Value.Trim()))
            {
                end = matches[nextIndex].Index;
                nextIndex++;
            }

            result.Add(new NovelChapter
            {
                NovelId = novelId,
                VolumeTitle = volumeTitle,
                VolumeOrder = Math.Max(1, volumeOrder),
                ChapterOrder = chapterOrder,
                Title = title,
                SourceStart = match.Index,
                SourceEnd = Math.Max(match.Index, end),
                CreatedAt = DateTime.UtcNow
            });
        }

        if (result.Count == 0)
        {
            result.Add(new NovelChapter
            {
                NovelId = novelId,
                VolumeTitle = volumeTitle,
                VolumeOrder = 1,
                ChapterOrder = 1,
                Title = volumeTitle ?? "正文",
                SourceStart = 0,
                SourceEnd = source.Length,
                CreatedAt = DateTime.UtcNow
            });
        }

        return result;
    }

    private static async Task BackfillScriptOffsetsAsync(
        AppDbContext db,
        long novelId,
        string source,
        IReadOnlyList<NovelChapter> chapters,
        CancellationToken cancellationToken)
    {
        var scripts = await db.ScriptLines
            .Where(x => x.NovelId == novelId)
            .OrderBy(x => x.Order)
            .ToListAsync(cancellationToken);

        var cursor = 0;
        foreach (var line in scripts)
        {
            var text = Normalize(line.Text);
            var position = string.IsNullOrWhiteSpace(text)
                ? -1
                : source.IndexOf(text, Math.Clamp(cursor, 0, source.Length), StringComparison.Ordinal);

            if (position < 0 && !string.IsNullOrWhiteSpace(text))
                position = source.IndexOf(text, StringComparison.Ordinal);

            line.SourceStart = position;
            line.SourceEnd = position >= 0 ? Math.Min(source.Length, position + text.Length) : -1;
            if (position >= 0)
            {
                cursor = line.SourceEnd;
                line.ChapterId = chapters.LastOrDefault(x => x.SourceStart <= position && x.SourceEnd > position)?.Id
                                 ?? chapters.LastOrDefault(x => x.SourceStart <= position)?.Id;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Normalize(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    [GeneratedRegex(@"(?m)^[ \t]*(?<title>(?:第[零〇一二三四五六七八九十百千万两\d]+[卷部篇辑章回节][^\r\n]{0,50}|(?:卷|部|篇)[零〇一二三四五六七八九十百千万两\d]+[^\r\n]{0,40}))[ \t]*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(?:第[零〇一二三四五六七八九十百千万两\d]+[卷部篇辑]|(?:卷|部|篇)[零〇一二三四五六七八九十百千万两\d]+)", RegexOptions.CultureInvariant)]
    private static partial Regex VolumeRegex();
}
