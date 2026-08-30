using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Jobs;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Api.Endpoints;

/// <summary>
/// 小说音频资产管理 API：分页查询、试听、下载、重生成、删除及整书合并。
/// </summary>
public static class AudioEndpoints
{
    public static IEndpointRouteBuilder MapAudioEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/audio").WithTags("Audio");

        group.MapGet("/novels/{novelId:long}", async (
            long novelId,
            AppDbContext db,
            int page = 1,
            int pageSize = 20,
            string? keyword = null,
            string? speaker = null,
            string? status = null) =>
        {
            var novel = await db.Novels.FindAsync(novelId);
            if (novel is null) return Results.NotFound();

            NormalizePaging(ref page, ref pageSize);

            var allQuery = db.ScriptLines.AsNoTracking().Where(x => x.NovelId == novelId);
            var total = await allQuery.CountAsync();
            var completed = await allQuery.CountAsync(x =>
                x.Status == "Completed" &&
                x.AudioFile != null);

            var query = allQuery;

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var value = keyword.Trim();
                query = query.Where(x =>
                    x.Text.Contains(value) ||
                    x.Speaker.Contains(value) ||
                    (x.Emotion != null && x.Emotion.Contains(value)));
            }

            if (!string.IsNullOrWhiteSpace(speaker))
                query = query.Where(x => x.Speaker == speaker);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(x => x.Status == status);

            var filteredTotal = await query.CountAsync();
            var pageItems = await query
                .OrderBy(x => x.Order)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var merged = Path.GetFullPath($"storage/output/novel-{novelId}.mp3");
            return Results.Ok(new
            {
                novelId,
                novel.Title,
                mergedExists = File.Exists(merged),
                mergedFile = File.Exists(merged) ? merged : null,
                total,
                completed,
                filteredTotal,
                page,
                pageSize,
                speakers = await allQuery.Select(x => x.Speaker).Distinct().OrderBy(x => x).ToListAsync(),
                segments = pageItems.Select(x =>
                {
                    var fullPath = string.IsNullOrWhiteSpace(x.AudioFile)
                        ? null
                        : Path.GetFullPath(x.AudioFile);
                    return new
                    {
                        x.Id,
                        x.Order,
                        x.Speaker,
                        x.Text,
                        x.Emotion,
                        x.Status,
                        x.AudioFile,
                        Exists = fullPath is not null && File.Exists(fullPath)
                    };
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

            if (!string.IsNullOrWhiteSpace(line.AudioFile))
            {
                var fullPath = Path.GetFullPath(line.AudioFile);
                if (File.Exists(fullPath))
                    File.Delete(fullPath);
            }

            line.AudioFile = null;
            line.Status = "Pending";
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        group.MapGet("/segments/{scriptLineId:long}/play", async (long scriptLineId, AppDbContext db) =>
        {
            var line = await db.ScriptLines.FindAsync(scriptLineId);
            if (line?.AudioFile is null)
                return Results.NotFound();

            return PhysicalFile(line.AudioFile, "audio/wav");
        });

        group.MapGet("/segments/{scriptLineId:long}/download", async (long scriptLineId, AppDbContext db) =>
        {
            var line = await db.ScriptLines.FindAsync(scriptLineId);
            if (line?.AudioFile is null)
                return Results.NotFound();

            return PhysicalFile(
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

            return PhysicalFile($"storage/output/novel-{novelId}.mp3", "audio/mpeg");
        });

        group.MapGet("/novels/{novelId:long}/download", async (long novelId, AppDbContext db) =>
        {
            var novel = await db.Novels.FindAsync(novelId);
            if (novel is null) return Results.NotFound();

            return PhysicalFile(
                $"storage/output/novel-{novelId}.mp3",
                "audio/mpeg",
                SanitizeFileName(novel.Title) + ".mp3");
        });

        return app;
    }

    private static IResult PhysicalFile(string filePath, string contentType, string? downloadName = null)
    {
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
            return Results.NotFound(new { message = "音频文件不存在。", path = fullPath });

        // 使用 FileStream 返回物理文件，避免 Results.File(string) 将相对路径按 WebRoot/VirtualFile 解析。
        var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        return Results.File(
            stream,
            contentType,
            fileDownloadName: downloadName,
            enableRangeProcessing: true);
    }

    private static void NormalizePaging(ref int page, ref int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 5, 200);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value;
    }
}