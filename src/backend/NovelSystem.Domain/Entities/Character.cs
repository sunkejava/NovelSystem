using NovelSystem.Domain.Common;

namespace NovelSystem.Domain.Entities;

/// <summary>
/// 小说人物。优先通过 VoiceProfileId 绑定可复用音色配置。
/// VoiceFile 保留用于兼容旧数据库和已有数据。
/// </summary>
public sealed class Character : Entity
{
    public long NovelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public string? Personality { get; set; }
    public string? Description { get; set; }
    public long? VoiceProfileId { get; set; }
    public string? VoiceFile { get; set; }
}