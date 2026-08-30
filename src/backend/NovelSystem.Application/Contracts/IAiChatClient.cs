namespace NovelSystem.Application.Contracts;

/// <summary>大语言模型调用抽象，默认由 llama.cpp 实现。</summary>
public interface IAiChatClient
{
    Task<string> ChatAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 要求模型返回 JSON。用于小说结构解析等结构化任务，
    /// 实现层可启用 llama.cpp response_format 与 Prompt Cache。
    /// </summary>
    Task<string> ChatJsonAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}