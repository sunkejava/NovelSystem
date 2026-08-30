using System.Net.Http.Json;
using System.Text.Json;
using NovelSystem.Application.Contracts;
using NovelSystem.Infrastructure.Persistence;
using NovelSystem.Infrastructure.Services;

namespace NovelSystem.Infrastructure.AI;

/// <summary>
/// llama.cpp OpenAI-compatible Chat Completions 客户端。
/// 对重复系统提示启用 Prompt/KV Cache，减少长小说分块解析时的重复 Prefill。
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

        var maxTokens = int.TryParse(settings.Get("AiAnalysisMaxTokens", "16384"), out var configuredMaxTokens)
            ? Math.Clamp(configuredMaxTokens, 512, 65536)
            : 16384;

        var cachePrompt = !bool.TryParse(settings.Get("AiCachePrompt", "true"), out var configuredCache)
                          || configuredCache;

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        object payload = jsonMode
            ? new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.1,
                max_tokens = maxTokens,
                stream = false,
                cache_prompt = cachePrompt,
                response_format = new { type = "json_object" }
            }
            : new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.25,
                max_tokens = maxTokens,
                stream = false,
                cache_prompt = cachePrompt
            };

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