using NovelSystem.Domain.Common;
namespace NovelSystem.Domain.Entities;
/// <summary>AI 生成的新小说。</summary>
public sealed class GeneratedNovel : Entity { public string Title { get; set; }=string.Empty; public long? SourceNovelId { get; set; } public long? StyleId { get; set; } public string Prompt { get; set; }=string.Empty; public string Content { get; set; }=string.Empty; public DateTime CreatedAt { get; set; }=DateTime.UtcNow; }