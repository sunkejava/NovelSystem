using NovelSystem.Domain.Common;

namespace NovelSystem.Domain.Entities;

/// <summary>小说卷/章节结构，SourceStart/SourceEnd 对应原始正文字符偏移。</summary>
public sealed class NovelChapter : Entity
{
    public long NovelId { get; set; }
    public string? VolumeTitle { get; set; }
    public int VolumeOrder { get; set; }
    public int ChapterOrder { get; set; }
    public string Title { get; set; } = string.Empty;
    public int SourceStart { get; set; }
    public int SourceEnd { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
