using NovelSystem.Domain.Common;
namespace NovelSystem.Domain.Entities;
/// <summary>可用于多角色 TTS 的单条脚本。</summary>
public sealed class ScriptLine : Entity { public long NovelId { get; set; } public long? CharacterId { get; set; } public int Order { get; set; } public string Speaker { get; set; }="旁白"; public string Text { get; set; }=string.Empty; public string? Emotion { get; set; } public string? AudioFile { get; set; } public string Status { get; set; }="Pending"; }