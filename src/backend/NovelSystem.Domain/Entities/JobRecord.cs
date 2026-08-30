using NovelSystem.Domain.Common;

namespace NovelSystem.Domain.Entities;

/// <summary>
/// 后台任务持久化记录。
/// Checkpoint 表示已完成步骤，TotalSteps 表示总步骤，RetryCount 表示断点重试次数。
/// 时间统计字段用于前端展示开始时间、完成时间、总耗时、平均单步耗时和预计完成时间。
/// </summary>
public sealed class JobRecord : Entity
{
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = "Queued";
    public int Progress { get; set; }
    public int Checkpoint { get; set; }
    public int TotalSteps { get; set; }
    public int RetryCount { get; set; }
    public string? Payload { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }

    /// <summary>任务创建时间（UTC）。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>任务第一次真正开始执行的时间（UTC）。</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>任务完成、失败或停止的时间（UTC）。重新排队时会清空。</summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>从首次开始执行至当前/结束时的总墙钟耗时，单位毫秒。</summary>
    public long ElapsedMilliseconds { get; set; }

    /// <summary>根据已完成步骤计算出的平均单步耗时，单位毫秒。</summary>
    public long AverageStepMilliseconds { get; set; }

    /// <summary>根据当前平均速度估算的预计完成时间（UTC）。</summary>
    public DateTime? EstimatedCompletionAt { get; set; }
}