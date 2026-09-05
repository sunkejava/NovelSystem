using NovelSystem.Domain.Common;

namespace NovelSystem.Domain.Entities;

/// <summary>小说发音词典。Pattern 为原文词语，Replacement 为送入 TTS 的读音替换文本。</summary>
public sealed class PronunciationEntry : Entity
{
    public long NovelId { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public string Replacement { get; set; } = string.Empty;
    public string? Note { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
