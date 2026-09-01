using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Api.Contracts;
using NovelSystem.Application.Contracts;
using NovelSystem.Application.Models;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Jobs;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Api.Endpoints;

/// <summary>写作手法学习任务、风格管理、AI 创作和生成小说回流 API。</summary>
public static class WritingEndpoints
{
    public static IEndpointRouteBuilder MapWritingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/writing").WithTags("Writing");

        group.MapPost("/learn/{novelId:long}", async (long novelId, AppDbContext db, JobQueue queue) =>
        {
            var novel = await db.Novels.FindAsync(novelId);
            if (novel is null) return Results.NotFound();

            var exists = await db.Jobs.AnyAsync(x =>
                x.Type == "LearnWritingStyle" &&
                x.Payload != null &&
                x.Payload.Contains($"\"novelId\":{novelId}") &&
                (x.Status == "Queued" || x.Status == "Running" || x.Status == "Stopping"));

            if (exists)
                return Results.Conflict(new { message = "该小说已有正在执行的写作风格学习任务。" });

            var payload = JsonSerializer.Serialize(new { novelId });
            var job = new JobRecord { Type = "LearnWritingStyle", Payload = payload };
            db.Jobs.Add(job);
            await db.SaveChangesAsync();

            queue.Enqueue(new JobMessage(job.Id, job.Type, payload));
            return Results.Ok(job);
        });

        group.MapGet("/styles", async (
            AppDbContext db,
            int page = 1,
            int pageSize = 12,
            string? keyword = null) =>
        {
            NormalizePaging(ref page, ref pageSize);

            var query = db.WritingStyles.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var value = keyword.Trim();
                query = query.Where(style =>
                    style.Name.Contains(value) ||
                    style.Summary.Contains(value) ||
                    (style.NovelId != null &&
                     db.Novels.Any(n => n.Id == style.NovelId && n.Title.Contains(value))));
            }

            var total = await query.CountAsync();
            var styles = await query.OrderByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var novelIds = styles.Where(x => x.NovelId.HasValue)
                .Select(x => x.NovelId!.Value)
                .Distinct()
                .ToList();

            var novels = await db.Novels
                .Where(x => novelIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Title);

            var items = styles.Select(style => new
            {
                style.Id,
                style.NovelId,
                NovelTitle = style.NovelId.HasValue && novels.TryGetValue(style.NovelId.Value, out var title) ? title : null,
                style.Name,
                style.Summary,
                style.PromptTemplate
            });

            return Results.Ok(new { items, total, page, pageSize });
        });

        group.MapGet("/styles/options", async (AppDbContext db) =>
            await db.WritingStyles.AsNoTracking()
                .OrderByDescending(x => x.Id)
                .Select(x => new { x.Id, x.Name })
                .ToListAsync());

        group.MapPut("/styles/{id:long}", async (long id, UpdateWritingStyleRequest request, AppDbContext db) =>
        {
            var style = await db.WritingStyles.FindAsync(id);
            if (style is null) return Results.NotFound();

            style.Name = request.Name.Trim();
            style.Summary = request.Summary;
            style.PromptTemplate = request.PromptTemplate;
            await db.SaveChangesAsync();
            return Results.Ok(style);
        });

        group.MapDelete("/styles/{id:long}", async (long id, AppDbContext db) =>
        {
            var style = await db.WritingStyles.FindAsync(id);
            if (style is null) return Results.NotFound();

            var inUse = await db.GeneratedNovels.AnyAsync(x => x.StyleId == id);
            if (inUse)
                return Results.Conflict(new { message = "该写作风格已被生成小说引用，暂不能删除。" });

            db.WritingStyles.Remove(style);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        group.MapPost("/generate", async (
            GenerateNovelRequest request,
            AppDbContext db,
            JobQueue queue) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest(new { message = "小说标题不能为空。" });
            if (request.TargetWords <= 0)
                return Results.BadRequest(new { message = "目标字数必须大于 0。" });
            if (request.ChapterCount <= 0)
                return Results.BadRequest(new { message = "章节数必须大于 0。" });

            // 不对目标字数、章节数设置业务上限。
            // 真正的长篇生成由后台任务按章节拆分执行，避免一次请求被模型 max_tokens 截断。
            var generated = new GeneratedNovel
            {
                Title = request.Title.Trim(),
                StyleId = request.StyleId,
                SourceNovelId = request.SourceNovelId,
                Prompt = request.Prompt,
                Genre = request.Genre,
                TargetWords = request.TargetWords,
                ChapterCount = request.ChapterCount,
                PointOfView = request.PointOfView,
                Tone = request.Tone,
                Content = string.Empty
            };

            db.GeneratedNovels.Add(generated);
            await db.SaveChangesAsync();

            var payload = JsonSerializer.Serialize(new
            {
                generatedNovelId = generated.Id,
                request.Title,
                request.StyleId,
                request.SourceNovelId,
                request.Prompt,
                request.Genre,
                request.TargetWords,
                request.ChapterCount,
                request.PointOfView,
                request.Tone
            });

            var job = new JobRecord
            {
                Type = "GenerateNovel",
                Payload = payload,
                TotalSteps = request.ChapterCount + 1
            };
            db.Jobs.Add(job);
            await db.SaveChangesAsync();

            queue.Enqueue(new JobMessage(job.Id, job.Type, payload));

            return Results.Ok(new
            {
                jobId = job.Id,
                generatedNovelId = generated.Id,
                status = job.Status,
                progress = job.Progress,
                totalSteps = job.TotalSteps
            });
        });

        group.MapGet("/generated", async (
            AppDbContext db,
            int page = 1,
            int pageSize = 12,
            string? keyword = null) =>
        {
            NormalizePaging(ref page, ref pageSize);
            var query = db.GeneratedNovels.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var value = keyword.Trim();
                query = query.Where(x =>
                    x.Title.Contains(value) ||
                    x.Prompt.Contains(value) ||
                    x.Content.Contains(value));
            }

            var total = await query.CountAsync();
            var items = await query.OrderByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Results.Ok(new { items, total, page, pageSize });
        });

        group.MapGet("/generated/{id:long}", async (long id, AppDbContext db) =>
        {
            var novel = await db.GeneratedNovels.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (novel is null)
                return Results.NotFound();

            var job = await db.Jobs.AsNoTracking()
                .Where(x => x.Type == "GenerateNovel" &&
                            x.Payload != null &&
                            x.Payload.Contains($"\"generatedNovelId\":{id}"))
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            return Results.Ok(new
            {
                novel.Id,
                novel.Title,
                novel.StyleId,
                novel.SourceNovelId,
                novel.Prompt,
                novel.Genre,
                novel.TargetWords,
                novel.ChapterCount,
                novel.PointOfView,
                novel.Tone,
                novel.Outline,
                novel.Content,
                createdAt = JobTimingCalculator.ToUtcIso(novel.CreatedAt),
                job = job is null ? null : new
                {
                    job.Id,
                    job.Status,
                    job.Progress,
                    job.Checkpoint,
                    job.TotalSteps,
                    job.Error,
                    job.ElapsedMilliseconds,
                    estimatedCompletionAt = JobTimingCalculator.ToUtcIso(job.EstimatedCompletionAt)
                }
            });
        });

        group.MapGet("/generated/{id:long}/download", async (long id, AppDbContext db) =>
        {
            var novel = await db.GeneratedNovels.FindAsync(id);
            return novel is null
                ? Results.NotFound()
                : Results.File(Encoding.UTF8.GetBytes(novel.Content), "text/plain", novel.Title + ".txt");
        });

        group.MapPost("/generated/{id:long}/publish", async (long id, AppDbContext db) =>
        {
            var generated = await db.GeneratedNovels.FindAsync(id);
            if (generated is null) return Results.NotFound();

            var novel = new Novel { Title = generated.Title, SourceFile = "AI生成", Content = generated.Content };
            db.Novels.Add(novel);
            await db.SaveChangesAsync();
            return Results.Ok(novel);
        });

        return app;
    }

    private static void NormalizePaging(ref int page, ref int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 5, 200);
    }
}