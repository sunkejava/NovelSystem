using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Jobs;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Api.Endpoints;

/// <summary>
/// 小说音频资产管理 API：片段查询、试听、下载、重生成、删除及整书合并。
/// </summary>
public static class AudioEndpoints
{
    public static IEndpointRouteBuilder MapAudioEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/audio").WithTags("Audio");

        group.MapGet("/novels/{novelId:long}", async (long novelId, AppDbContext db) =>
        {
            var novel = await db.Novels.FindAsync(novelId);
            if (novel is null) return Results.NotFound();

            var lines = await db.ScriptLines
                .Where(x => x.NovelId == novelId)
                .OrderBy(x => x.Order)
                .ToListAsync();

            var merged = $"storage/output/novel-{novelId}.mp3";
            return Results.Ok(new
            {
                novelId,
                novel.Title,
                mergedExists = File.Exists(merged),
                mergedFile = File.Exists(merged) ? merged : null,
                total = lines.Count,
                completed = lines.Count(x => x.Status == "Completed" && x.AudioFile != null && File.Exists(x.AudioFile)),
                segments = lines.Select(x => new
                {
                    x.Id,
                    x.Order,
                    x.Speaker,
                    x.Text,
                    x.Emotion,
                    x.Status,
                    x.AudioFile,
                    Exists = !string.IsNullOrWhiteSpace(x.AudioFile) && File.Exists(x.AudioFile)
                })
            });
        });

        group.MapPost("/segments/{scriptLineId:long}/generate", async (
            long scriptLineId,
            AppDbContext db,
            JobQueue queue) =>
        {
            var line = await db.ScriptLines.FindAsync(scriptLineId);
            if (line is null) return Results.NotFound();

            var payload = JsonSerializer.Serialize(new { novelId = line.NovelId, scriptLineId });
            var job = new JobRecord { Type = "GenerateAudioSegment", Payload = payload };
            db.Jobs.Add(job);
            await db.SaveChangesAsync();

            queue.Enqueue(new JobMessage(job.Id, job.Type, payload));
            return Results.Ok(job);
        });

        group.MapDelete("/segments/{scriptLineId:long}", async (long scriptLineId, AppDbContext db) =>
        {
            var line = await db.ScriptLines.FindAsync(scriptLineId);
            if (line is null) return Results.NotFound();

            if (!string.IsNullOrWhiteSpace(line.AudioFile) && File.Exists(line.AudioFile))
                File.Delete(line.AudioFile);

            line.AudioFile = null;
            line.Status = "Pending";
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        group.MapGet("/segments/{scriptLineId:long}/play", async (long scriptLineId, AppDbContext db) =>
        {
            var line = await db.ScriptLines.FindAsync(scriptLineId);
            if (line?.AudioFile is null || !File.Exists(line.AudioFile))
                return Results.NotFound();

            return Results.File(line.AudioFile, "audio/wav", enableRangeProcessing: true);
        });

        group.MapGet("/segments/{scriptLineId:long}/download", async (long scriptLineId, AppDbContext db) =>
        {
            var line = await db.ScriptLines.FindAsync(scriptLineId);
            if (line?.AudioFile is null || !File.Exists(line.AudioFile))
                return Results.NotFound();

            return Results.File(
                line.AudioFile,
                "audio/wav",
                $"{line.Order:D6}-{SanitizeFileName(line.Speaker)}.wav");
        });

        group.MapPost("/novels/{novelId:long}/merge", async (
            long novelId,
            AppDbContext db,
            JobQueue queue) =>
        {
            if (!await db.Novels.AnyAsync(x => x.Id == novelId))
                return Results.NotFound();

            var payload = JsonSerializer.Serialize(new { novelId });
            var job = new JobRecord { Type = "MergeAudio", Payload = payload };
            db.Jobs.Add(job);
            await db.SaveChangesAsync();

            queue.Enqueue(new JobMessage(job.Id, job.Type, payload));
            return Results.Ok(job);
        });

        group.MapGet("/novels/{novelId:long}/play", async (long novelId, AppDbContext db) =>
        {
            var novel = await db.Novels.FindAsync(novelId);
            if (novel is null) return Results.NotFound();

            var file = $"storage/output/novel-{novelId}.mp3";
            return !File.Exists(file)
                ? Results.NotFound()
                : Results.File(file, "audio/mpeg", enableRangeProcessing: true);
        });

        group.MapGet("/novels/{novelId:long}/download", async (long novelId, AppDbContext db) =>
        {
            var novel = await db.Novels.FindAsync(novelId);
            if (novel is null) return Results.NotFound();

            var file = $"storage/output/novel-{novelId}.mp3";
            return !File.Exists(file)
                ? Results.NotFound()
                : Results.File(file, "audio/mpeg", SanitizeFileName(novel.Title) + ".mp3");
        });

        return app;
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value;
    }
}