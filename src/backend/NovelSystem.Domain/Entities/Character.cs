using NovelSystem.Domain.Common;
namespace NovelSystem.Domain.Entities;
/// <summary>小说人物及人物音色绑定。</summary>
public sealed class Character : Entity { public long NovelId { get; set; } public string Name { get; set; }=string.Empty; public string? Gender { get; set; } public string? Personality { get; set; } public string? Description { get; set; } public string? VoiceFile { get; set; } }