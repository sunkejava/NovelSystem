using System.Net.Http.Json;
using System.Text.Json;
using NovelSystem.Application.Contracts;
using NovelSystem.Infrastructure.Persistence;
using NovelSystem.Infrastructure.Services;

namespace NovelSystem.Infrastructure.AI;

/// <summary>
/// llama.cpp OpenAI-compatible Chat Completions 客户端。
/// 默认关闭 Qwen thinking，并复用 HTTP 连接和 prompt cache，以贴近 llama.cpp WebUI 的快速解析行为。
/// </summary>
public sealed class LlamaCppChatClient(IHttpClientFactory httpClientFactory, AppDbContext db) : IAiChatClient
{
    public Task<string> ChatAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
        => SendAsync(systemPrompt, userPrompt, jsonMode: false, cancellationToken);

    public Task<string> ChatJsonAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
        => SendAsync(systemPrompt, userPrompt, jsonMode: true, cancellationToken);

    private async Task<string> SendAsync(
        string systemPrompt,
        string userPrompt,
        bool jsonMode,
        CancellationToken cancellationToken)
    {
        var settings = new SettingReader(db);
        var baseUrl = settings.Get("AiBaseUrl", "http://127.0.0.1:8080/v1").TrimEnd('/');
        var model = settings.Get("AiModel", "local-model");

        var timeoutSeconds = int.TryParse(settings.Get("AiTimeoutSeconds", "120"), out var timeout)
            ? Math.Clamp(timeout, 10, 3600)
            : 120;

        var maxTokens = int.TryParse(settings.Get("AiAnalysisMaxTokens", "8192"), out var configuredMaxTokens)
            ? Math.Clamp(configuredMaxTokens, 512, 65536)
            : 8192;

        var cachePrompt = !bool.TryParse(settings.Get("AiCachePrompt", "true"), out var configuredCache)
                          || configuredCache;

        var enableThinking = bool.TryParse(settings.Get("AiEnableThinking", "false"), out var thinking)
                             && thinking;

        var useJsonResponseFormat = bool.TryParse(
                                        settings.Get("AiUseJsonResponseFormat", "false"),
                                        out var jsonFormat)
                                    && jsonFormat;

        var client = httpClientFactory.CreateClient("llama.cpp");
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        // Qwen 系列在未显式关闭 thinking 时可能先产生大量推理 token。
        // chat_template_kwargs 与 llama.cpp WebUI 的 enable_thinking 开关保持一致。
        var common = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = jsonMode ? 0.0 : 0.25,
            max_tokens = maxTokens,
            stream = false,
            cache_prompt = cachePrompt,
            chat_template_kwargs = new
            {
                enable_thinking = enableThinking
            }
        };

        object payload;
        if (jsonMode && useJsonResponseFormat)
        {
            payload = new
            {
                common.model,
                common.messages,
                common.temperature,
                common.max_tokens,
                common.stream,
                common.cache_prompt,
                common.chat_template_kwargs,
                response_format = new { type = "json_object" }
            };
        }
        else
        {
            payload = common;
        }

        using var response = await client.PostAsJsonAsync(
            $"{baseUrl}/chat/completions",
            payload,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        return json.RootElement
                   .GetProperty("choices")[0]
                   .GetProperty("message")
                   .GetProperty("content")
                   .GetString()
               ?? string.Empty;
    }
}