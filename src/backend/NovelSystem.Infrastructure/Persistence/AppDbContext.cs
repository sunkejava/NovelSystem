using Microsoft.EntityFrameworkCore;
using NovelSystem.Domain.Entities;

namespace NovelSystem.Infrastructure.Persistence;

/// <summary>NovelSystem EF Core 数据上下文。</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Novel> Novels => Set<Novel>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<ScriptLine> ScriptLines => Set<ScriptLine>();
    public DbSet<JobRecord> Jobs => Set<JobRecord>();
    public DbSet<SystemSetting> Settings => Set<SystemSetting>();
    public DbSet<WritingStyle> WritingStyles => Set<WritingStyle>();
    public DbSet<GeneratedNovel> GeneratedNovels => Set<GeneratedNovel>();
    public DbSet<VoiceProfile> VoiceProfiles => Set<VoiceProfile>();
    public DbSet<AiTokenUsage> AiTokenUsages => Set<AiTokenUsage>();
    public DbSet<AiAnalysisError> AiAnalysisErrors => Set<AiAnalysisError>();
    public DbSet<NovelChapter> NovelChapters => Set<NovelChapter>();
    public DbSet<PronunciationEntry> PronunciationEntries => Set<PronunciationEntry>();
    public DbSet<NovelQaIssue> NovelQaIssues => Set<NovelQaIssue>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await RestoreAddedScriptOrderAndOffsetsAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// LLM 正常情况下会按原文返回 s 数组，但 JSON 修复、强约束重试或模型本身仍可能偶发交换元素。
    /// 在新增脚本真正写库前，利用“脚本文本必须来自原文”这一强约束恢复本批顺序，
    /// 同时写入 SourceStart/SourceEnd，并根据原文偏移自动关联 ChapterId。
    /// </summary>
    private async Task RestoreAddedScriptOrderAndOffsetsAsync(CancellationToken cancellationToken)
    {
        var added = ChangeTracker.Entries<ScriptLine>()
            .Where(x => x.State == EntityState.Added && !string.IsNullOrWhiteSpace(x.Entity.Text))
            .GroupBy(x => x.Entity.NovelId)
            .ToList();

        if (added.Count == 0)
            return;

        foreach (var novelGroup in added)
        {
            var entries = novelGroup.OrderBy(x => x.Entity.Order).ToList();
            if (entries.Count == 0)
                continue;

            var trackedNovel = ChangeTracker.Entries<Novel>()
                .FirstOrDefault(x => x.Entity.Id == novelGroup.Key)?.Entity;
            if (trackedNovel is null || string.IsNullOrWhiteSpace(trackedNovel.Content))
                continue;

            var source = NormalizeSourceText(trackedNovel.Content);
            var previousText = await ScriptLines.AsNoTracking()
                .Where(x => x.NovelId == novelGroup.Key)
                .OrderByDescending(x => x.Order)
                .Select(x => x.Text)
                .FirstOrDefaultAsync(cancellationToken);

            var searchStart = 0;
            if (!string.IsNullOrWhiteSpace(previousText))
            {
                var previous = NormalizeSourceText(previousText);
                var previousIndex = source.LastIndexOf(previous, StringComparison.Ordinal);
                if (previousIndex >= 0)
                    searchStart = Math.Min(source.Length, previousIndex + previous.Length);
            }

            var cursor = searchStart;
            var positioned = new List<PositionedScript>(entries.Count);
            for (var originalIndex = 0; originalIndex < entries.Count; originalIndex++)
            {
                var entry = entries[originalIndex];
                var normalizedText = NormalizeSourceText(entry.Entity.Text);
                var position = FindSourcePosition(source, normalizedText, cursor);

                // 如果严格从 cursor 往后未找到，再从当前批次锚点找一次，避免某条模型文本略有差异
                // 导致后续全部无法定位；仍不从全文开头搜索，避免误匹配前文重复对白。
                if (position < 0)
                    position = FindSourcePosition(source, normalizedText, searchStart);

                if (position >= 0)
                {
                    entry.Entity.SourceStart = position;
                    entry.Entity.SourceEnd = Math.Min(source.Length, position + normalizedText.Length);
                    cursor = Math.Max(cursor, entry.Entity.SourceEnd);
                }
                else
                {
                    entry.Entity.SourceStart = -1;
                    entry.Entity.SourceEnd = -1;
                }

                positioned.Add(new PositionedScript(entry, originalIndex, position));
            }

            if (positioned.Count(x => x.SourcePosition >= 0) >= 2)
            {
                var ordered = positioned
                    .OrderBy(x => x.SourcePosition < 0 ? int.MaxValue : x.SourcePosition)
                    .ThenBy(x => x.OriginalIndex)
                    .ToList();

                var firstOrder = entries.Min(x => x.Entity.Order);
                for (var index = 0; index < ordered.Count; index++)
                    ordered[index].Entry.Entity.Order = firstOrder + index;
            }

            var chapters = await NovelChapters.AsNoTracking()
                .Where(x => x.NovelId == novelGroup.Key)
                .OrderBy(x => x.SourceStart)
                .ToListAsync(cancellationToken);

            if (chapters.Count > 0)
            {
                foreach (var item in positioned.Where(x => x.Entry.Entity.SourceStart >= 0))
                {
                    var sourceStart = item.Entry.Entity.SourceStart;
                    var chapter = chapters.LastOrDefault(x => x.SourceStart <= sourceStart && x.SourceEnd > sourceStart)
                                  ?? chapters.LastOrDefault(x => x.SourceStart <= sourceStart);
                    item.Entry.Entity.ChapterId = chapter?.Id;
                }
            }
        }
    }

    private static int FindSourcePosition(string source, string text, int searchStart)
    {
        if (string.IsNullOrWhiteSpace(text))
            return -1;
        return source.IndexOf(text, Math.Clamp(searchStart, 0, source.Length), StringComparison.Ordinal);
    }

    private static string NormalizeSourceText(string value)
        => value.Trim()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemSetting>().HasIndex(x => x.Key).IsUnique();
        modelBuilder.Entity<Character>().HasIndex(x => new { x.NovelId, x.Name });
        modelBuilder.Entity<ScriptLine>().HasIndex(x => new { x.NovelId, x.Order });
        modelBuilder.Entity<ScriptLine>().HasIndex(x => new { x.NovelId, x.SourceStart });
        modelBuilder.Entity<NovelChapter>().HasIndex(x => new { x.NovelId, x.ChapterOrder });
        modelBuilder.Entity<PronunciationEntry>().HasIndex(x => new { x.NovelId, x.Pattern });
        modelBuilder.Entity<NovelQaIssue>().HasIndex(x => new { x.NovelId, x.Resolved, x.Severity });
        modelBuilder.Entity<VoiceProfile>().HasIndex(x => x.Name);
        modelBuilder.Entity<AiTokenUsage>().HasIndex(x => new { x.NovelId, x.JobId, x.Operation });
        modelBuilder.Entity<AiTokenUsage>().HasIndex(x => x.CreatedAt);
        modelBuilder.Entity<AiAnalysisError>().HasIndex(x => new { x.NovelId, x.JobId, x.ChunkIndex });
        modelBuilder.Entity<AiAnalysisError>().HasIndex(x => x.CreatedAt);
        base.OnModelCreating(modelBuilder);
    }

    private sealed record PositionedScript(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<ScriptLine> Entry,
        int OriginalIndex,
        int SourcePosition);
}
