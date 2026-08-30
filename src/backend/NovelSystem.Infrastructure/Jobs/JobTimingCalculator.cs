using NovelSystem.Domain.Entities;

namespace NovelSystem.Infrastructure.Jobs;

/// <summary>
/// 统一计算任务耗时和 ETA。
/// ETA 基于“当前已完成步骤的平均耗时 × 剩余步骤数”动态估算，
/// 因本地 LLM/TTS 每一步耗时会变化，所以这是近似值而不是承诺完成时间。
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
        var elapsed = now - job.StartedAt.Value;
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

        // 防止异常数据导致 DateTime 溢出。
        var maxMilliseconds = TimeSpan.FromDays(365).TotalMilliseconds;
        remainingMilliseconds = Math.Min(remainingMilliseconds, (long)maxMilliseconds);
        job.EstimatedCompletionAt = now.AddMilliseconds(remainingMilliseconds);
    }

    public static void MarkTerminal(JobRecord job)
    {
        job.FinishedAt = DateTime.UtcNow;
        Refresh(job, terminal: true);
    }
}