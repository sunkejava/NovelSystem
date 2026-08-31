using NovelSystem.Domain.Common;

namespace NovelSystem.Domain.Entities;

/// <summary>
/// LLM 结构化解析异常样本。保存错误源文本、模型原始输出和解析错误，便于后续定位模型/Prompt/分块问题。
/// </summary>
public sealed class AiAnalysisError : Entity
{
    public long NovelId { get; set; }
    public long JobId { get; set; }
    public int ChunkIndex { get; set; }
    public int ChunkTotal { get; set; }
    public int RetryDepth { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;
    public string? RawResponse { get; set; }
    public string Error { get; set; } = string.Empty;
    public bool Recovered { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}