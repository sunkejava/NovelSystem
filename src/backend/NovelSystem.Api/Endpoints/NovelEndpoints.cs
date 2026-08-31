using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Api.Contracts;
using NovelSystem.Api.Utils;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Jobs;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Api.Endpoints;

/// <summary>小说上传、分页查询、编辑、删除、详情、脚本查询、解析和音频任务 API。</summary>
public static class NovelEndpoints
{
    public static IEndpointRouteBuilder MapNovelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/novels").WithTags("Novels");

        group.MapGet("/", async (
            AppDbContext db,
            int page = 1,
            int pageSize = 12,
            string? keyword = null,
            string? status = null) =>
        {
            NormalizePaging(ref page, ref pageSize);
            var query = db.Novels.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var value = keyword.Trim();
                query = query.Where(x => x.Title.Contains(value) || x.SourceFile.Contains(value));
            }

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(x => x.Status == status);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.Id,
                    x.Title,
                    x.SourceFile,
                    x.Status,
                    x.CreatedAt
                })
                .ToListAsync();

            return Results.Ok(new { items, total, page, pageSize });
        });

        group.MapPost("/upload", async (HttpRequest request, AppDbContext db) =>
        {
            var form = await request.ReadFormAsync();
            var file = form.Files["file"] ?? throw new BadHttpRequestException("必须上传小说文件。");

            await using var stream = file.OpenReadStream();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var content = TextEncodingDetector.Decode(memory.ToArray());

            var novel = new Novel
            {
                Title = form["title"].FirstOrDefault() ?? Path.GetFileNameWithoutExtension(file.FileName),
                SourceFile = file.FileName,
                Content = content
            };

            db.Novels.Add(novel);
            await db.SaveChangesAsync();
            return Results.Ok(novel);
        });

        group.MapGet("/{id:long}", async (long id, AppDbContext db) =>
        {
            var novel = await db.Novels.FindAsync(id);
            if (novel is null) return Results.NotFound();

            return Results.Ok(new
            {
                novel,
                characters = await db.Characters.Where(x => x.NovelId == id).OrderBy(x => x.Id).ToListAsync(),
                scriptCount = await db.ScriptLines.CountAsync(x => x.NovelId == id)
            });
        });

        group.MapGet("/{id:long}/scripts", async (
            long id,
            AppDbContext db,
            int page = 1,
            int pageSize = 20,
            string? keyword = null,
            string? speaker = null,
            string? status = null) =>
        {
            NormalizePaging(ref page, ref pageSize);
            var query = db.ScriptLines.AsNoTracking().Where(x => x.NovelId == id);

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

            var total = await query.CountAsync();
            var items = await query.OrderBy(x => x.Order)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var speakers = await db.ScriptLines.AsNoTracking()
                .Where(x => x.NovelId == id)
                .Select(x => x.Speaker)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            return Results.Ok(new { items, total, page, pageSize, speakers });
        });

        group.MapPut("/{id:long}/narrator-voice", async (long id, UpdateNarratorVoiceRequest request, AppDbContext db) =>
        {
            var novel = await db.Novels.FindAsync(id);
            if (novel is null) return Results.NotFound();

            if (request.VoiceProfileId.HasValue &&
                !await db.VoiceProfiles.AnyAsync(x => x.Id == request.VoiceProfileId.Value))
                return Results.BadRequest(new { message = "所选旁白音色档案不存在。" });

            novel.NarratorVoiceProfileId = request.VoiceProfileId;
            await db.SaveChangesAsync();
            return Results.Ok(new { novel.Id, novel.NarratorVoiceProfileId });
        });

        group.MapPut("/{id:long}", async (long id, UpdateNovelRequest request, AppDbContext db) =>
        {
            var novel = await db.Novels.FindAsync(id);
            if (novel is null) return Results.NotFound();

            novel.Title = request.Title.Trim();
            novel.Content = request.Content;
            await db.SaveChangesAsync();
            return Results.Ok(novel);
        });

        group.MapDelete("/{id:long}", async (long id, AppDbContext db) =>
        {
            var novel = await db.Novels.FindAsync(id);
            if (novel is null) return Results.NotFound();

            var running = await db.Jobs.AnyAsync(x =>
                x.Payload != null &&
                x.Payload.Contains($"\"novelId\":{id}") &&
                (x.Status == "Running" || x.Status == "Stopping"));
            if (running) return Results.Conflict(new { message = "该小说仍有正在执行的任务，请先停止任务。" });

            db.ScriptLines.RemoveRange(db.ScriptLines.Where(x => x.NovelId == id));
            db.Characters.RemoveRange(db.Characters.Where(x => x.NovelId == id));
            db.WritingStyles.RemoveRange(db.WritingStyles.Where(x => x.NovelId == id));
            db.Novels.Remove(novel);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        group.MapPost("/{id:long}/analyze", (long id, AppDbContext db, JobQueue queue) =>
            EnqueueAsync(db, queue, "AnalyzeNovel", id));

        group.MapPost("/{id:long}/audio", (long id, AppDbContext db, JobQueue queue) =>
            EnqueueAsync(db, queue, "GenerateAudio", id));

        return app;
    }

    private static async Task<IResult> EnqueueAsync(AppDbContext db, JobQueue queue, string type, long novelId)
    {
        var payload = JsonSerializer.Serialize(new { novelId });
        var job = new JobRecord { Type = type, Payload = payload };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        queue.Enqueue(new JobMessage(job.Id, type, payload));
        return Results.Ok(job);
    }

    private static void NormalizePaging(ref int page, ref int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 5, 200);
    }
}