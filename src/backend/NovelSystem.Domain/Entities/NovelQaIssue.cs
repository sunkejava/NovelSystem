using NovelSystem.Domain.Common;

namespace NovelSystem.Domain.Entities;

/// <summary>有声书生产质量检测问题。</summary>
public sealed class NovelQaIssue : Entity
{
    public long NovelId { get; set; }
    public long? ScriptLineId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = "Warning";
    public string Message { get; set; } = string.Empty;
    public bool Resolved { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
