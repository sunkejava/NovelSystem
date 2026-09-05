using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NovelSystem.Application.Contracts;
using NovelSystem.Infrastructure.AI;
using NovelSystem.Infrastructure.Jobs;
using NovelSystem.Infrastructure.Persistence;
using NovelSystem.Infrastructure.Services;
using NovelSystem.Infrastructure.Tts;

namespace NovelSystem.Infrastructure;

/// <summary>Infrastructure 层统一依赖注入入口。</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddNovelSystemInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

        services.AddHttpClient("llama.cpp")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
                PooledConnectionLifetime = TimeSpan.FromHours(1),
                MaxConnectionsPerServer = 4
            });

        services.AddHttpClient();
        services.AddScoped<IAiChatClient, LlamaCppChatClient>();
        services.AddScoped<Qwen3TtsClient>();
        services.AddScoped<ITtsClient, PronunciationTtsClient>();
        services.AddScoped<INovelAnalysisService, NovelAnalysisService>();
        services.AddSingleton<JobQueue>();
        services.AddHostedService<JobWorker>();
        return services;
    }
}