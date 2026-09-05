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
        await EnsureNovelNarratorVoiceSchemaAsync(db, cancellationToken);
        await EnsureJobCheckpointSchemaAsync(db, cancellationToken);
        await EnsureAiTokenUsageSchemaAsync(db, cancellationToken);
        await EnsureAiTokenUsageEstimatedSchemaAsync(db, cancellationToken);
        await EnsureVoiceSemanticSchemaAsync(db, cancellationToken);
        await EnsureAiAnalysisErrorSchemaAsync(db, cancellationToken);
        await EnsureGeneratedNovelSchemaAsync(db, cancellationToken);
        await EnsureProductionSchemaAsync(db, cancellationToken);

        var defaults = new Dictionary<string, string>
        {
            ["AiBaseUrl"] = "http://127.0.0.1:8080/v1",
            ["AiModel"] = "local-model",
            ["AiTimeoutSeconds"] = "120",
            ["AiChunkSize"] = "8000",
            ["AiCachePrompt"] = "true",
            ["AiAnalysisCachePrompt"] = "false",
            ["AiEnableThinking"] = "false",
            ["AiUseJsonResponseFormat"] = "false",
            ["AiAnalysisMaxTokens"] = "16384",
            ["AiProvider"] = "LocalLlamaCpp",
            ["AiApiKey"] = "",
            ["AiStyleChunkSize"] = "16000",
            ["AiStyleSampleChunks"] = "12",
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
            ["FfmpegPath"] = "ffmpeg",
            ["FfprobePath"] = "ffprobe"
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

    private static async Task EnsureNovelNarratorVoiceSchemaAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var columns = await GetColumnsAsync(db, "Novels", cancellationToken);
        if (!columns.Contains("NarratorVoiceProfileId", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Novels" ADD COLUMN "NarratorVoiceProfileId" INTEGER NULL;""", cancellationToken);
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
        if (!columns.Contains("QueuedAt", StringComparer.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Jobs" ADD COLUMN "QueuedAt" TEXT NULL;""", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("""UPDATE "Jobs" SET "QueuedAt" = "CreatedAt" WHERE "QueuedAt" IS NULL;""", cancellationToken);
        }
    }

    private static async Task EnsureAiTokenUsageSchemaAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "AiTokenUsages" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AiTokenUsages" PRIMARY KEY AUTOINCREMENT,
                "NovelId" INTEGER NULL,
                "JobId" INTEGER NULL,
                "Operation" TEXT NOT NULL,
                "ChunkIndex" INTEGER NULL,
                "ChunkTotal" INTEGER NULL,
                "Model" TEXT NOT NULL,
                "PromptTokens" INTEGER NOT NULL,
                "CompletionTokens" INTEGER NOT NULL,
                "TotalTokens" INTEGER NOT NULL,
                "CachedPromptTokens" INTEGER NOT NULL,
                "ElapsedMilliseconds" INTEGER NOT NULL,
                "PromptTokensPerSecond" REAL NOT NULL,
                "CompletionTokensPerSecond" REAL NOT NULL,
                "InputCharacters" INTEGER NOT NULL,
                "OutputCharacters" INTEGER NOT NULL,
                "Success" INTEGER NOT NULL,
                "Error" TEXT NULL,
                "CreatedAt" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_AiTokenUsages_NovelId_JobId_Operation" ON "AiTokenUsages" ("NovelId","JobId","Operation");
            CREATE INDEX IF NOT EXISTS "IX_AiTokenUsages_CreatedAt" ON "AiTokenUsages" ("CreatedAt");
            """, cancellationToken);
    }

    private static async Task EnsureAiTokenUsageEstimatedSchemaAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var columns = await GetColumnsAsync(db, "AiTokenUsages", cancellationToken);
        if (!columns.Contains("IsEstimated", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "AiTokenUsages" ADD COLUMN "IsEstimated" INTEGER NOT NULL DEFAULT 0;""", cancellationToken);
    }

    private static async Task EnsureVoiceSemanticSchemaAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var columns = await GetColumnsAsync(db, "VoiceProfiles", cancellationToken);
        if (!columns.Contains("VoiceDescription", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "VoiceProfiles" ADD COLUMN "VoiceDescription" TEXT NULL;""", cancellationToken);
        if (!columns.Contains("VoiceTags", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "VoiceProfiles" ADD COLUMN "VoiceTags" TEXT NULL;""", cancellationToken);
    }

    private static async Task EnsureAiAnalysisErrorSchemaAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "AiAnalysisErrors" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AiAnalysisErrors" PRIMARY KEY AUTOINCREMENT,
                "NovelId" INTEGER NOT NULL,
                "JobId" INTEGER NOT NULL,
                "ChunkIndex" INTEGER NOT NULL,
                "ChunkTotal" INTEGER NOT NULL,
                "RetryDepth" INTEGER NOT NULL,
                "Stage" TEXT NOT NULL,
                "SourceText" TEXT NOT NULL,
                "RawResponse" TEXT NULL,
                "Error" TEXT NOT NULL,
                "Recovered" INTEGER NOT NULL,
                "CreatedAt" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_AiAnalysisErrors_NovelId_JobId_ChunkIndex" ON "AiAnalysisErrors" ("NovelId","JobId","ChunkIndex");
            CREATE INDEX IF NOT EXISTS "IX_AiAnalysisErrors_CreatedAt" ON "AiAnalysisErrors" ("CreatedAt");
            """, cancellationToken);
    }

    private static async Task EnsureGeneratedNovelSchemaAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var columns = await GetColumnsAsync(db, "GeneratedNovels", cancellationToken);
        var alters = new List<string>();
        if (!columns.Contains("Genre", StringComparer.OrdinalIgnoreCase)) alters.Add("""ALTER TABLE "GeneratedNovels" ADD COLUMN "Genre" TEXT NULL;""");
        if (!columns.Contains("TargetWords", StringComparer.OrdinalIgnoreCase)) alters.Add("""ALTER TABLE "GeneratedNovels" ADD COLUMN "TargetWords" INTEGER NOT NULL DEFAULT 0;""");
        if (!columns.Contains("ChapterCount", StringComparer.OrdinalIgnoreCase)) alters.Add("""ALTER TABLE "GeneratedNovels" ADD COLUMN "ChapterCount" INTEGER NOT NULL DEFAULT 0;""");
        if (!columns.Contains("PointOfView", StringComparer.OrdinalIgnoreCase)) alters.Add("""ALTER TABLE "GeneratedNovels" ADD COLUMN "PointOfView" TEXT NULL;""");
        if (!columns.Contains("Tone", StringComparer.OrdinalIgnoreCase)) alters.Add("""ALTER TABLE "GeneratedNovels" ADD COLUMN "Tone" TEXT NULL;""");
        if (!columns.Contains("Outline", StringComparer.OrdinalIgnoreCase)) alters.Add("""ALTER TABLE "GeneratedNovels" ADD COLUMN "Outline" TEXT NULL;""");
        foreach (var sql in alters)
            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    /// <summary>专业制作能力：章节、原文偏移、发音词典、QA、音频时间轴与 A/B 版本。</summary>
    private static async Task EnsureProductionSchemaAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "NovelChapters" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_NovelChapters" PRIMARY KEY AUTOINCREMENT,
                "NovelId" INTEGER NOT NULL,
                "VolumeTitle" TEXT NULL,
                "VolumeOrder" INTEGER NOT NULL DEFAULT 0,
                "ChapterOrder" INTEGER NOT NULL,
                "Title" TEXT NOT NULL,
                "SourceStart" INTEGER NOT NULL,
                "SourceEnd" INTEGER NOT NULL,
                "CreatedAt" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_NovelChapters_NovelId_ChapterOrder" ON "NovelChapters" ("NovelId","ChapterOrder");

            CREATE TABLE IF NOT EXISTS "PronunciationEntries" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_PronunciationEntries" PRIMARY KEY AUTOINCREMENT,
                "NovelId" INTEGER NOT NULL,
                "Pattern" TEXT NOT NULL,
                "Replacement" TEXT NOT NULL,
                "Note" TEXT NULL,
                "IsEnabled" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_PronunciationEntries_NovelId_Pattern" ON "PronunciationEntries" ("NovelId","Pattern");

            CREATE TABLE IF NOT EXISTS "NovelQaIssues" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_NovelQaIssues" PRIMARY KEY AUTOINCREMENT,
                "NovelId" INTEGER NOT NULL,
                "ScriptLineId" INTEGER NULL,
                "Type" TEXT NOT NULL,
                "Severity" TEXT NOT NULL,
                "Message" TEXT NOT NULL,
                "Resolved" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_NovelQaIssues_NovelId_Resolved_Severity" ON "NovelQaIssues" ("NovelId","Resolved","Severity");

            CREATE TABLE IF NOT EXISTS "ScriptAudioVersions" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ScriptAudioVersions" PRIMARY KEY AUTOINCREMENT,
                "NovelId" INTEGER NOT NULL,
                "ScriptLineId" INTEGER NOT NULL,
                "VersionNo" INTEGER NOT NULL,
                "FilePath" TEXT NOT NULL,
                "DurationMs" INTEGER NOT NULL DEFAULT 0,
                "IsSelected" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ScriptAudioVersions_ScriptLineId_VersionNo" ON "ScriptAudioVersions" ("ScriptLineId","VersionNo");
            CREATE INDEX IF NOT EXISTS "IX_ScriptAudioVersions_NovelId_ScriptLineId_IsSelected" ON "ScriptAudioVersions" ("NovelId","ScriptLineId","IsSelected");
            """, cancellationToken);

        var scriptColumns = await GetColumnsAsync(db, "ScriptLines", cancellationToken);
        if (!scriptColumns.Contains("ChapterId", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "ScriptLines" ADD COLUMN "ChapterId" INTEGER NULL;""", cancellationToken);
        if (!scriptColumns.Contains("SourceStart", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "ScriptLines" ADD COLUMN "SourceStart" INTEGER NOT NULL DEFAULT -1;""", cancellationToken);
        if (!scriptColumns.Contains("SourceEnd", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "ScriptLines" ADD COLUMN "SourceEnd" INTEGER NOT NULL DEFAULT -1;""", cancellationToken);
        if (!scriptColumns.Contains("AudioStartMs", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "ScriptLines" ADD COLUMN "AudioStartMs" INTEGER NULL;""", cancellationToken);
        if (!scriptColumns.Contains("AudioEndMs", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "ScriptLines" ADD COLUMN "AudioEndMs" INTEGER NULL;""", cancellationToken);

        await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_ScriptLines_NovelId_SourceStart" ON "ScriptLines" ("NovelId","SourceStart");""", cancellationToken);
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
