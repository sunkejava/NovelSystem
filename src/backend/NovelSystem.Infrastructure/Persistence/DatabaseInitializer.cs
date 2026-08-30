using Microsoft.EntityFrameworkCore;
using NovelSystem.Domain.Entities;

namespace NovelSystem.Infrastructure.Persistence;

/// <summary>
/// 初始化数据库、默认配置以及轻量级 SQLite 结构升级。
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureVoiceProfileSchemaAsync(db, cancellationToken);
        await EnsureJobCheckpointSchemaAsync(db, cancellationToken);

        var defaults = new Dictionary<string, string>
        {
            ["AiBaseUrl"] = "http://127.0.0.1:8080/v1",
            ["AiModel"] = "local-model",
            ["AiTimeoutSeconds"] = "120",
            ["AiChunkSize"] = "12000",
            ["AiCachePrompt"] = "true",
            ["AiAnalysisMaxTokens"] = "16384",
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

        var characterColumns = await GetColumnsAsync(db, "Characters", cancellationToken);
        if (!characterColumns.Contains("VoiceProfileId", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Characters" ADD COLUMN "VoiceProfileId" INTEGER NULL;""", cancellationToken);
    }

    private static async Task EnsureJobCheckpointSchemaAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var columns = await GetColumnsAsync(db, "Jobs", cancellationToken);

        if (!columns.Contains("Checkpoint", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Jobs" ADD COLUMN "Checkpoint" INTEGER NOT NULL DEFAULT 0;""", cancellationToken);
        if (!columns.Contains("TotalSteps", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Jobs" ADD COLUMN "TotalSteps" INTEGER NOT NULL DEFAULT 0;""", cancellationToken);
        if (!columns.Contains("RetryCount", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Jobs" ADD COLUMN "RetryCount" INTEGER NOT NULL DEFAULT 0;""", cancellationToken);
        if (!columns.Contains("ElapsedMilliseconds", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Jobs" ADD COLUMN "ElapsedMilliseconds" INTEGER NOT NULL DEFAULT 0;""", cancellationToken);
        if (!columns.Contains("AverageStepMilliseconds", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Jobs" ADD COLUMN "AverageStepMilliseconds" INTEGER NOT NULL DEFAULT 0;""", cancellationToken);
        if (!columns.Contains("EstimatedCompletionAt", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Jobs" ADD COLUMN "EstimatedCompletionAt" TEXT NULL;""", cancellationToken);
    }

    private static async Task<List<string>> GetColumnsAsync(AppDbContext db, string tableName, CancellationToken cancellationToken)
    {
        var columns = new List<string>();
        await using var command = db.Database.GetDbConnection().CreateCommand();

        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync(cancellationToken);

        command.CommandText = $"PRAGMA table_info('{tableName}');";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            columns.Add(reader.GetString(1));

        return columns;
    }
}