using NovelSystem.Domain.Common;

namespace NovelSystem.Domain.Entities;

/// <summary>AI 声音导演生成的 BGM / SFX 编排建议。先作为可审核 Cue 存储，确认后再绑定真实音频素材。</summary>
public sealed class SoundDesignCue : Entity
{
    public long NovelId { get; set; }
    public long? ChapterId { get; set; }
    public long? ScriptLineId { get; set; }
    public string Type { get; set; } = "Sfx";
    public string Scene { get; set; } = string.Empty;
    public string Mood { get; set; } = string.Empty;
    public int Intensity { get; set; } = 50;
    public string Keywords { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public bool Approved { get; set; }
    public bool Applied { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
