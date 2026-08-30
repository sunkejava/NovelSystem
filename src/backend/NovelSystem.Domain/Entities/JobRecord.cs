using NovelSystem.Domain.Common;

namespace NovelSystem.Domain.Entities;

/// <summary>
/// 后台任务持久化记录。
/// Checkpoint 表示已完成的步骤数，TotalSteps 表示总步骤数，RetryCount 记录断点重试次数。
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}