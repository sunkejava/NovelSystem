using System.Net.Http.Json;
using System.Text.Json;
using NovelSystem.Application.Contracts;
using NovelSystem.Application.Models;
using NovelSystem.Domain.Entities;
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
        => SendAsync(systemPrompt, userPrompt, jsonMode: false, context: null, cancellationToken);

    public Task<string> ChatJsonAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
        => SendAsync(systemPrompt, userPrompt, jsonMode: true, context: null, cancellationToken);

    public Task<string> ChatTrackedAsync(
        string systemPrompt,
        string userPrompt,
        AiCallContext context,
        CancellationToken cancellationToken = default)
        => SendAsync(systemPrompt, userPrompt, jsonMode: false, context, cancellationToken);

    public Task<string> ChatJsonTrackedAsync(
        string systemPrompt,
        string userPrompt,
        AiCallContext context,
        CancellationToken cancellationToken = default)
        => SendAsync(systemPrompt, userPrompt, jsonMode: true, context, cancellationToken, forceJsonResponseFormat: false);

    public Task<string> ChatJsonStrictTrackedAsync(
        string systemPrompt,
        string userPrompt,
        AiCallContext context,
        CancellationToken cancellationToken = default)
        => SendAsync(systemPrompt, userPrompt, jsonMode: true, context, cancellationToken, forceJsonResponseFormat: true);

    private async Task<string> SendAsync(
        string systemPrompt,
        string userPrompt,
        bool jsonMode,
        AiCallContext? context,
        CancellationToken cancellationToken,
        bool forceJsonResponseFormat = false)
    {
        var settings = new SettingReader(db);
        var baseUrl = settings.Get("AiBaseUrl", "http://127.0.0.1:8080/v1").TrimEnd('/');
        var model = settings.Get("AiModel", "local-model");

        var timeoutSeconds = int.TryParse(settings.Get("AiTimeoutSeconds", "120"), out var timeout)
            ? Math.Clamp(timeout, 10, 3600)
            : 120;

        var configuredMaxTokens = int.TryParse(settings.Get("AiAnalysisMaxTokens", "16384"), out var configured)
            ? Math.Clamp(configured, 512, 65536)
            : 16384;

        // 中文小说结构化提取的输出长度经常接近甚至超过输入字符数。
        // 如果 max_tokens 过小，llama.cpp 会在 JSON 数组闭合前直接截断。
        // jsonMode 下根据当前输入长度动态预留输出空间，用户配置作为最低值。
        var estimatedJsonTokens = Math.Clamp(userPrompt.Length * 2, 4096, 32768);
        var maxTokens = jsonMode
            ? Math.Max(configuredMaxTokens, estimatedJsonTokens)
            : configuredMaxTokens;

        var generalCachePrompt = !bool.TryParse(settings.Get("AiCachePrompt", "true"), out var configuredCache)
                                 || configuredCache;

        // 小说解析分块彼此独立，不应该继承上一块的 KV/slot 状态。
        // 默认关闭解析专用 Prompt Cache，避免连续数十/数百次请求后 slot/cache 状态累积，
        // 导致后续分块 prompt/decode 耗时逐步上升。
        var analysisCachePrompt = bool.TryParse(
                                      settings.Get("AiAnalysisCachePrompt", "false"),
                                      out var configuredAnalysisCache)
                                  && configuredAnalysisCache;

        var cachePrompt = jsonMode ? analysisCachePrompt : generalCachePrompt;

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
            // -1 表示让 server 为每次独立请求选择空闲 slot，不固定到某个历史 slot。
            id_slot = -1,
            chat_template_kwargs = new
            {
                enable_thinking = enableThinking
            }
        };

        object payload;
        if (jsonMode && (useJsonResponseFormat || forceJsonResponseFormat))
        {
            payload = new
            {
                common.model,
                common.messages,
                common.temperature,
                common.max_tokens,
                common.stream,
                common.cache_prompt,
                common.id_slot,
                common.chat_template_kwargs,
                response_format = new { type = "json_object" }
            };
        }
        else
        {
            payload = common;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        string? body = null;

        try
        {
            using var response = await client.PostAsJsonAsync(
                $"{baseUrl}/chat/completions",
                payload,
                cancellationToken);

            body = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            stopwatch.Stop();

            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var content = root.GetProperty("choices")[0]
                              .GetProperty("message")
                              .GetProperty("content")
                              .GetString()
                          ?? string.Empty;

            if (context is not null)
                await SaveUsageAsync(
                    context,
                    model,
                    systemPrompt.Length + userPrompt.Length,
                    content.Length,
                    root,
                    stopwatch.ElapsedMilliseconds,
                    true,
                    null,
                    cancellationToken);

            return content;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            if (context is not null)
            {
                try
                {
                    JsonElement? root = null;
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        using var errorJson = JsonDocument.Parse(body);
                        root = errorJson.RootElement.Clone();
                    }

                    await SaveUsageAsync(
                        context,
                        model,
                        systemPrompt.Length + userPrompt.Length,
                        0,
                        root,
                        stopwatch.ElapsedMilliseconds,
                        false,
                        ex.Message,
                        CancellationToken.None);
                }
                catch
                {
                    // 统计失败不能覆盖原始 AI 异常。
                }
            }

            throw;
        }
    }


    private async Task SaveUsageAsync(
        AiCallContext context,
        string model,
        int inputCharacters,
        int outputCharacters,
        JsonElement? root,
        long elapsedMilliseconds,
        bool success,
        string? error,
        CancellationToken cancellationToken)
    {
        var promptTokens = 0;
        var completionTokens = 0;
        var totalTokens = 0;
        var cachedPromptTokens = 0;
        var promptTokensPerSecond = 0d;
        var completionTokensPerSecond = 0d;

        if (root is { } value)
        {
            if (value.TryGetProperty("usage", out var usage))
            {
                promptTokens = GetInt(usage, "prompt_tokens");
                completionTokens = GetInt(usage, "completion_tokens");
                totalTokens = GetInt(usage, "total_tokens");
                if (usage.TryGetProperty("prompt_tokens_details", out var details))
                    cachedPromptTokens = GetInt(details, "cached_tokens");
            }

            if (value.TryGetProperty("timings", out var timings))
            {
                promptTokensPerSecond = GetDouble(timings, "prompt_per_second");
                completionTokensPerSecond = GetDouble(timings, "predicted_per_second");
                if (cachedPromptTokens == 0)
                    cachedPromptTokens = GetInt(timings, "cache_n");
            }
        }

        if (totalTokens == 0)
            totalTokens = promptTokens + completionTokens;

        db.AiTokenUsages.Add(new AiTokenUsage
        {
            NovelId = context.NovelId,
            JobId = context.JobId,
            Operation = context.Operation,
            ChunkIndex = context.ChunkIndex,
            ChunkTotal = context.ChunkTotal,
            Model = model,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            CachedPromptTokens = cachedPromptTokens,
            ElapsedMilliseconds = elapsedMilliseconds,
            PromptTokensPerSecond = promptTokensPerSecond,
            CompletionTokensPerSecond = completionTokensPerSecond,
            InputCharacters = inputCharacters,
            OutputCharacters = outputCharacters,
            Success = success,
            Error = error,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static int GetInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.TryGetInt32(out var value) ? value : 0;

    private static double GetDouble(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.TryGetDouble(out var value) ? value : 0d;

}