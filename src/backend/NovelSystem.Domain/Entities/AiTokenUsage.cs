using NovelSystem.Domain.Common;

namespace NovelSystem.Domain.Entities;

/// <summary>
/// 单次本地 AI 调用 Token / 性能统计记录。
/// 用于按小说、任务、分块和操作类型分析模型资源消耗。
/// </summary>
public sealed class AiTokenUsage : Entity
{
    public long? NovelId { get; set; }
    public long? JobId { get; set; }
    public string Operation { get; set; } = "Unknown";
    public int? ChunkIndex { get; set; }
    public int? ChunkTotal { get; set; }
    public string Model { get; set; } = string.Empty;

    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public int CachedPromptTokens { get; set; }

    public long ElapsedMilliseconds { get; set; }
    public double PromptTokensPerSecond { get; set; }
    public double CompletionTokensPerSecond { get; set; }

    public int InputCharacters { get; set; }
    public int OutputCharacters { get; set; }
    public bool Success { get; set; } = true;
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}