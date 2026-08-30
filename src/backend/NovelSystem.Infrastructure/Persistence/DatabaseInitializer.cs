using Microsoft.EntityFrameworkCore;
using NovelSystem.Domain.Entities;

namespace NovelSystem.Infrastructure.Persistence;

/// <summary>
/// 初始化数据库、默认配置以及轻量级 SQLite 结构升级。
/// 正式生产环境后续可切换到 EF Core Migrations。
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureVoiceProfileSchemaAsync(db, cancellationToken);

        var defaults = new Dictionary<string, string>
        {
            ["AiBaseUrl"] = "http://127.0.0.1:8080/v1",
            ["AiModel"] = "local-model",
            ["TtsBaseUrl"] = "http://127.0.0.1:8000",
            ["TtsUploadEndpoint"] = "/gradio_api/upload",
            ["TtsVoiceCloneSubmitEndpoint"] = "/gradio_api/call/v2/run_voice_clone",
            ["TtsVoiceCloneResultEndpoint"] = "/gradio_api/call/run_voice_clone/{eventId}",
            ["TtsSavePromptSubmitEndpoint"] = "/gradio_api/call/v2/save_prompt",
            ["TtsSavePromptResultEndpoint"] = "/gradio_api/call/save_prompt/{eventId}",
            ["TtsPromptGenSubmitEndpoint"] = "/gradio_api/call/v2/load_prompt_and_gen",
            ["TtsPromptGenResultEndpoint"] = "/gradio_api/call/load_prompt_and_gen/{eventId}",
            ["TtsTimeoutSeconds"] = "300",
            ["TtsDefaultLanguage"] = "Chinese",
            ["VoiceDirectory"] = "voices",
            ["PromptDirectory"] = "storage/prompts",
            ["FfmpegPath"] = "ffmpeg"
        };

        foreach (var item in defaults)
            if (!db.Settings.Any(x => x.Key == item.Key))
                db.Settings.Add(new SystemSetting { Key = item.Key, Value = item.Value });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureVoiceProfileSchemaAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "VoiceProfiles" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_VoiceProfiles" PRIMARY KEY AUTOINCREMENT,
                "Name" TEXT NOT NULL,
                "ReferenceAudioFile" TEXT NOT NULL,
                "ReferenceText" TEXT NOT NULL,
                "UseXVector" INTEGER NOT NULL,
                "Language" TEXT NOT NULL,
                "PromptFile" TEXT NULL,
                "Status" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            """, cancellationToken);

        var columns = new List<string>();
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            if (command.Connection!.State != System.Data.ConnectionState.Open)
                await command.Connection.OpenAsync(cancellationToken);
            command.CommandText = "PRAGMA table_info('Characters');";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                columns.Add(reader.GetString(1));
        }

        if (!columns.Contains("VoiceProfileId", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE "Characters" ADD COLUMN "VoiceProfileId" INTEGER NULL;", cancellationToken);
    }
}