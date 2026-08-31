using NovelSystem.Domain.Common;
namespace NovelSystem.Domain.Entities;
/// <summary>AI 生成的新小说及其创作规划。</summary>
public sealed class GeneratedNovel : Entity
{
    public string Title { get; set; }=string.Empty;
    public long? SourceNovelId { get; set; }
    public long? StyleId { get; set; }
    public string Prompt { get; set; }=string.Empty;
    public string? Genre { get; set; }
    public int TargetWords { get; set; }
    public int ChapterCount { get; set; }
    public string? PointOfView { get; set; }
    public string? Tone { get; set; }
    public string? Outline { get; set; }
    public string Content { get; set; }=string.Empty;
    public DateTime CreatedAt { get; set; }=DateTime.UtcNow;
}