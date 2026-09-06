using NovelSystem.Domain.Common;

namespace NovelSystem.Domain.Entities;

/// <summary>多轨制作中的一个音频素材片段。</summary>
public sealed class ProductionTrackClip : Entity
{
    public long NovelId { get; set; }
    public long TrackId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public long DurationMs { get; set; }
    public double Volume { get; set; } = 1d;
    public int FadeInMs { get; set; }
    public int FadeOutMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
