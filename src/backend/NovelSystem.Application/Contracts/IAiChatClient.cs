using NovelSystem.Application.Models;

namespace NovelSystem.Application.Contracts;

/// <summary>大语言模型调用抽象，默认由 llama.cpp 实现。</summary>
public interface IAiChatClient
{
    Task<string> ChatAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);

    Task<string> ChatJsonAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);

    /// <summary>带业务上下文的普通文本调用，会持久化 Token / timings。</summary>
    Task<string> ChatTrackedAsync(
        string systemPrompt,
        string userPrompt,
        AiCallContext context,
        CancellationToken cancellationToken = default);

    /// <summary>带业务上下文的 JSON 调用，会持久化 Token / timings。</summary>
    Task<string> ChatJsonTrackedAsync(
        string systemPrompt,
        string userPrompt,
        AiCallContext context,
        CancellationToken cancellationToken = default);
}