using NovelSystem.Domain.Common;

namespace NovelSystem.Domain.Entities;

/// <summary>
/// 可复用的 Qwen3-TTS 音色配置。
/// ReferenceAudioFile 为参考 WAV，ReferenceText 必须与参考音频内容尽量一致。
/// PromptFile 为 /save_prompt 生成并下载到 NovelSystem 本地的提示文件。
/// </summary>
public sealed class VoiceProfile : Entity
{
    public string Name { get; set; } = string.Empty;
    public string ReferenceAudioFile { get; set; } = string.Empty;
    public string ReferenceText { get; set; } = string.Empty;
    public bool UseXVector { get; set; }
    public string Language { get; set; } = "Chinese";
    public string? PromptFile { get; set; }
    public string Status { get; set; } = "Ready";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}