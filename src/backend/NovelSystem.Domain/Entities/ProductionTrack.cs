using NovelSystem.Domain.Common;

namespace NovelSystem.Domain.Entities;

/// <summary>专业制作多轨轨道：对白、背景音乐、音效等。</summary>
public sealed class ProductionTrack : Entity
{
    public long NovelId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Voice / Bgm / Sfx。</summary>
    public string Type { get; set; } = "Bgm";
    public int Order { get; set; }
    public double Volume { get; set; } = 1d;
    public bool IsMuted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
