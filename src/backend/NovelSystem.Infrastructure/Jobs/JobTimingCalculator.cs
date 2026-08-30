using NovelSystem.Domain.Entities;

namespace NovelSystem.Infrastructure.Jobs;

/// <summary>
/// 统一计算任务耗时和 ETA。数据库中的 DateTime 始终按 UTC 语义处理，
/// 即使 SQLite 读取后 DateTime.Kind 变成 Unspecified，也不会被误当成本地时间。
/// </summary>
public static class JobTimingCalculator
{
    public static void Refresh(JobRecord job, bool terminal = false)
    {
        if (job.StartedAt is null)
        {
            job.ElapsedMilliseconds = 0;
            job.AverageStepMilliseconds = 0;
            job.EstimatedCompletionAt = null;
            return;
        }

        var now = DateTime.UtcNow;
        var startedAtUtc = EnsureUtc(job.StartedAt.Value);
        var elapsed = now - startedAtUtc;
        job.ElapsedMilliseconds = Math.Max(0L, (long)elapsed.TotalMilliseconds);

        if (job.Checkpoint > 0)
        {
            job.AverageStepMilliseconds = Math.Max(
                1L,
                job.ElapsedMilliseconds / Math.Max(job.Checkpoint, 1));
        }

        if (terminal || job.TotalSteps <= 0 || job.Checkpoint <= 0 || job.Checkpoint >= job.TotalSteps)
        {
            job.EstimatedCompletionAt = null;
            return;
        }

        var remainingSteps = job.TotalSteps - job.Checkpoint;
        var remainingMilliseconds = job.AverageStepMilliseconds * (long)remainingSteps;
        remainingMilliseconds = Math.Min(
            remainingMilliseconds,
            (long)TimeSpan.FromDays(365).TotalMilliseconds);

        job.EstimatedCompletionAt = now.AddMilliseconds(remainingMilliseconds);
    }

    public static void MarkTerminal(JobRecord job)
    {
        job.FinishedAt = DateTime.UtcNow;
        Refresh(job, terminal: true);
    }

    public static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    public static string ToUtcIso(DateTime value)
        => EnsureUtc(value).ToString("O");

    public static string? ToUtcIso(DateTime? value)
        => value.HasValue ? ToUtcIso(value.Value) : null;
}