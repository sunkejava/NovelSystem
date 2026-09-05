using NovelSystem.Domain.Common;

namespace NovelSystem.Domain.Entities;

/// <summary>可用于多角色 TTS 的单条脚本。</summary>
public sealed class ScriptLine : Entity
{
    public long NovelId { get; set; }
    public long? ChapterId { get; set; }
    public long? CharacterId { get; set; }
    public int Order { get; set; }
    public string Speaker { get; set; } = "旁白";
    public string Text { get; set; } = string.Empty;
    public string? Emotion { get; set; }

    /// <summary>脚本文本在小说原文中的起始字符偏移，-1 表示无法精确定位。</summary>
    public int SourceStart { get; set; } = -1;

    /// <summary>脚本文本在小说原文中的结束字符偏移（不含），-1 表示无法精确定位。</summary>
    public int SourceEnd { get; set; } = -1;

    /// <summary>合并音频中的起始毫秒位置；第一阶段先预留字段，后续音频探测后回填。</summary>
    public long? AudioStartMs { get; set; }

    /// <summary>合并音频中的结束毫秒位置。</summary>
    public long? AudioEndMs { get; set; }

    public string? AudioFile { get; set; }
    public string Status { get; set; } = "Pending";
}
