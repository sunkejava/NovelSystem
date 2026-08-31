using NovelSystem.Domain.Common;

namespace NovelSystem.Domain.Entities;

/// <summary>
/// 可复用的 Qwen3-TTS 音色配置。
/// PromptFile 是 Qwen3-TTS 从参考音频提取的可复用声音条件缓存，并不是自然语言描述。
/// VoiceDescription / VoiceTags 用于人物类型展示和 AI 自动匹配。
/// </summary>
public sealed class VoiceProfile : Entity
{
    public string Name { get; set; } = string.Empty;
    public string ReferenceAudioFile { get; set; } = string.Empty;
    public string ReferenceText { get; set; } = string.Empty;
    public bool UseXVector { get; set; }
    public string Language { get; set; } = "Chinese";
    public string? PromptFile { get; set; }
    public string? VoiceDescription { get; set; }
    public string? VoiceTags { get; set; }
    public string Status { get; set; } = "Ready";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}