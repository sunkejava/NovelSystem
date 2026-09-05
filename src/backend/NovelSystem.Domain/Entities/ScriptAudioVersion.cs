using NovelSystem.Domain.Common;

namespace NovelSystem.Domain.Entities;

/// <summary>单条脚本的可选音频版本，用于 A/B 试听与人工选片。</summary>
public sealed class ScriptAudioVersion : Entity
{
    public long NovelId { get; set; }
    public long ScriptLineId { get; set; }
    public int VersionNo { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public bool IsSelected { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
