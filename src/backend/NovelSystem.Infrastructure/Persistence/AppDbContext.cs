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

    public override async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        await RestoreAddedScriptOrderAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// LLM 正常情况下会按原文返回 s 数组，但 JSON 修复、强约束重试或模型本身仍可能偶发交换元素。
    /// 在新增脚本真正写库前，利用“脚本文本必须来自原文”这一强约束，根据原文位置恢复本批脚本顺序。
    /// 只处理 Added 实体，不影响音频状态更新、人物去重等后续操作。
    /// </summary>
    private async Task RestoreAddedScriptOrderAsync(CancellationToken cancellationToken)
    {
        var added = ChangeTracker.Entries<ScriptLine>()
            .Where(x => x.State == EntityState.Added && !string.IsNullOrWhiteSpace(x.Entity.Text))
            .GroupBy(x => x.Entity.NovelId)
            .ToList();

        if (added.Count == 0)
            return;

        foreach (var novelGroup in added)
        {
            var entries = novelGroup
                .OrderBy(x => x.Entity.Order)
                .ToList();
            if (entries.Count < 2)
                continue;

            // 小说解析服务会跟踪当前 Novel，因此优先直接使用内存中的完整原文，避免再次读取大字段。
            var trackedNovel = ChangeTracker.Entries<Novel>()
                .FirstOrDefault(x => x.Entity.Id == novelGroup.Key)
                ?.Entity;
            if (trackedNovel is null || string.IsNullOrWhiteSpace(trackedNovel.Content))
                continue;

            var source = trackedNovel.Content
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');

            // 用上一条已经落库的脚本作为当前批次的搜索锚点，避免相同台词在小说前文出现时匹配到旧位置。
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

            var positioned = entries
                .Select((entry, originalIndex) => new
                {
                    Entry = entry,
                    OriginalIndex = originalIndex,
                    SourcePosition = FindSourcePosition(source, entry.Entity.Text, searchStart)
                })
                .ToList();

            // 至少有两条能定位才值得重排；否则保持模型原始顺序，避免猜测性修改。
            if (positioned.Count(x => x.SourcePosition >= 0) < 2)
                continue;

            var ordered = positioned
                .OrderBy(x => x.SourcePosition < 0 ? int.MaxValue : x.SourcePosition)
                .ThenBy(x => x.OriginalIndex)
                .ToList();

            var firstOrder = entries.Min(x => x.Entity.Order);
            for (var index = 0; index < ordered.Count; index++)
                ordered[index].Entry.Entity.Order = firstOrder + index;
        }
    }

    private static int FindSourcePosition(string source, string text, int searchStart)
    {
        var normalized = NormalizeSourceText(text);
        if (string.IsNullOrWhiteSpace(normalized))
            return -1;

        return source.IndexOf(normalized, Math.Clamp(searchStart, 0, source.Length), StringComparison.Ordinal);
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
        modelBuilder.Entity<VoiceProfile>().HasIndex(x => x.Name);
        modelBuilder.Entity<AiTokenUsage>().HasIndex(x => new { x.NovelId, x.JobId, x.Operation });
        modelBuilder.Entity<AiTokenUsage>().HasIndex(x => x.CreatedAt);
        modelBuilder.Entity<AiAnalysisError>().HasIndex(x => new { x.NovelId, x.JobId, x.ChunkIndex });
        modelBuilder.Entity<AiAnalysisError>().HasIndex(x => x.CreatedAt);
        base.OnModelCreating(modelBuilder);
    }
}