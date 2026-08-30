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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemSetting>().HasIndex(x => x.Key).IsUnique();
        modelBuilder.Entity<Character>().HasIndex(x => new { x.NovelId, x.Name });
        modelBuilder.Entity<ScriptLine>().HasIndex(x => new { x.NovelId, x.Order });
        modelBuilder.Entity<VoiceProfile>().HasIndex(x => x.Name);
        modelBuilder.Entity<AiTokenUsage>().HasIndex(x => new { x.NovelId, x.JobId, x.Operation });
        modelBuilder.Entity<AiTokenUsage>().HasIndex(x => x.CreatedAt);
        base.OnModelCreating(modelBuilder);
    }
}