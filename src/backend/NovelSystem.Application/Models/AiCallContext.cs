namespace NovelSystem.Application.Models;

/// <summary>AI 调用业务上下文，用于 Token 统计关联小说、任务与分块。</summary>
public sealed record AiCallContext(
    long? NovelId = null,
    long? JobId = null,
    string Operation = "Unknown",
    int? ChunkIndex = null,
    int? ChunkTotal = null);
