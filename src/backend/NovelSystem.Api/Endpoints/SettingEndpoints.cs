using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Api.Endpoints;

/// <summary>本地 AI、TTS、音色目录和 FFmpeg 配置 API。</summary>
public static class SettingEndpoints
{
    public static IEndpointRouteBuilder MapSettingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").WithTags("Settings");

        group.MapGet("/", (AppDbContext db) =>
            db.Settings.ToDictionary(x => x.Key, x => x.Value));

        group.MapPut("/", async (Dictionary<string, string> values, AppDbContext db) =>
        {
            foreach (var item in values)
            {
                var entity = await db.Settings.FirstOrDefaultAsync(x => x.Key == item.Key);
                if (entity is null)
                    db.Settings.Add(new SystemSetting { Key = item.Key, Value = item.Value });
                else
                    entity.Value = item.Value;
            }
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        group.MapGet("/voices", (AppDbContext db) =>
        {
            var directory = db.Settings.FirstOrDefault(x => x.Key == "VoiceDirectory")?.Value ?? "voices";
            if (!Directory.Exists(directory)) return Results.Ok(Array.Empty<object>());

            return Results.Ok(Directory.EnumerateFiles(directory, "*.wav")
                .Select(path => new { name = Path.GetFileNameWithoutExtension(path), path }));
        });

        group.MapPost("/test-ai", async (AppDbContext db, IHttpClientFactory factory, CancellationToken ct) =>
        {
            var settings = db.Settings.ToDictionary(x => x.Key, x => x.Value);
            var baseUrl = settings.GetValueOrDefault("AiBaseUrl", "http://127.0.0.1:8080/v1").TrimEnd('/');
            var model = settings.GetValueOrDefault("AiModel", "local-model");
            var sw = Stopwatch.StartNew();

            try
            {
                using var client = factory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                using var response = await client.PostAsJsonAsync(
                    $"{baseUrl}/chat/completions",
                    new
                    {
                        model,
                        messages = new[] { new { role = "user", content = "只回复 OK" } },
                        temperature = 0,
                        max_tokens = 8,
                        stream = false
                    },
                    ct);

                var body = await response.Content.ReadAsStringAsync(ct);
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(body);
                var reply = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                sw.Stop();

                return Results.Ok(new { online = true, model, latencyMs = sw.ElapsedMilliseconds, reply });
            }
            catch (Exception ex)
            {
                sw.Stop();
                return Results.Ok(new { online = false, model, latencyMs = sw.ElapsedMilliseconds, error = ex.Message });
            }
        });

        group.MapPost("/test-tts", async (AppDbContext db, IHttpClientFactory factory, CancellationToken ct) =>
        {
            var settings = db.Settings.ToDictionary(x => x.Key, x => x.Value);
            var baseUrl = settings.GetValueOrDefault("TtsBaseUrl", "http://127.0.0.1:8000").TrimEnd('/');
            var sw = Stopwatch.StartNew();

            try
            {
                using var client = factory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                using var response = await client.GetAsync(baseUrl + "/", ct);
                sw.Stop();
                return Results.Ok(new
                {
                    online = response.IsSuccessStatusCode,
                    latencyMs = sw.ElapsedMilliseconds,
                    statusCode = (int)response.StatusCode
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                return Results.Ok(new { online = false, latencyMs = sw.ElapsedMilliseconds, error = ex.Message });
            }
        });

        group.MapGet("/ai-status", async (AppDbContext db, IHttpClientFactory factory, CancellationToken ct) =>
        {
            var settings = db.Settings.ToDictionary(x => x.Key, x => x.Value);
            var aiBaseUrl = settings.GetValueOrDefault("AiBaseUrl", "http://127.0.0.1:8080/v1").TrimEnd('/');
            var aiModel = settings.GetValueOrDefault("AiModel", "local-model");
            var ttsBaseUrl = settings.GetValueOrDefault("TtsBaseUrl", "http://127.0.0.1:8000").TrimEnd('/');

            async Task<object> Probe(string url, string name)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using var client = factory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(4);
                    using var response = await client.GetAsync(url, ct);
                    sw.Stop();
                    return new { name, online = response.IsSuccessStatusCode, latencyMs = sw.ElapsedMilliseconds };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new { name, online = false, latencyMs = sw.ElapsedMilliseconds, error = ex.Message };
                }
            }

            var llm = await Probe(aiBaseUrl + "/models", aiModel);
            var tts = await Probe(ttsBaseUrl + "/", "Qwen3-TTS");
            return Results.Ok(new { llm, tts });
        });

        return app;
    }
}