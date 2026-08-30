using Microsoft.EntityFrameworkCore;
using NovelSystem.Infrastructure.Jobs;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Api.Endpoints;

/// <summary>后台任务分页查询、停止、继续、失败重试、删除和结果下载 API。</summary>
public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/jobs").WithTags("Jobs");

        group.MapGet("/", async (
            AppDbContext db,
            int page = 1,
            int pageSize = 20,
            string? type = null,
            string? status = null,
            string? keyword = null) =>
        {
            NormalizePaging(ref page, ref pageSize);
            var query = db.Jobs.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(type))
                query = query.Where(x => x.Type == type);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(x => x.Status == status);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var value = keyword.Trim();
                query = query.Where(x =>
                    x.Type.Contains(value) ||
                    (x.Error != null && x.Error.Contains(value)));
            }

            var total = await query.CountAsync();
            var rawItems = await query
                .OrderByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = rawItems.Select(x => new
            {
                x.Id,
                x.Type,
                x.Status,
                x.Progress,
                x.Checkpoint,
                x.TotalSteps,
                x.RetryCount,
                x.Payload,
                x.Result,
                x.Error,
                createdAt = JobTimingCalculator.ToUtcIso(x.CreatedAt),
                startedAt = JobTimingCalculator.ToUtcIso(x.StartedAt),
                finishedAt = JobTimingCalculator.ToUtcIso(x.FinishedAt),
                x.ElapsedMilliseconds,
                x.AverageStepMilliseconds,
                estimatedCompletionAt = JobTimingCalculator.ToUtcIso(x.EstimatedCompletionAt)
            }).ToList();

            var summary = new
            {
                running = await db.Jobs.CountAsync(x => x.Status == "Running" || x.Status == "Queued" || x.Status == "Stopping"),
                completed = await db.Jobs.CountAsync(x => x.Status == "Completed"),
                failed = await db.Jobs.CountAsync(x => x.Status == "Failed")
            };

            return Results.Ok(new { items, total, page, pageSize, summary });
        });

        group.MapPost("/{id:long}/stop", async (long id, AppDbContext db) =>
        {
            var job = await db.Jobs.FindAsync(id);
            if (job is null) return Results.NotFound();
            if (job.Status is "Completed" or "Failed" or "Stopped")
                return Results.Ok(job);

            job.Status = "Stopping";
            await db.SaveChangesAsync();
            return Results.Ok(job);
        });

        group.MapPost("/{id:long}/continue", async (long id, AppDbContext db, JobQueue queue) =>
        {
            var job = await db.Jobs.FindAsync(id);
            if (job is null) return Results.NotFound();
            if (job.Status != "Stopped")
                return Results.Conflict(new { message = "只有已停止任务可以继续。" });

            return await RequeueAsync(job, db, queue, false);
        });

        group.MapPost("/{id:long}/retry", async (long id, AppDbContext db, JobQueue queue) =>
        {
            var job = await db.Jobs.FindAsync(id);
            if (job is null) return Results.NotFound();
            if (job.Status != "Failed")
                return Results.Conflict(new { message = "只有失败任务可以执行断点重试。" });

            return await RequeueAsync(job, db, queue, true);
        });

        group.MapDelete("/{id:long}", async (long id, AppDbContext db) =>
        {
            var job = await db.Jobs.FindAsync(id);
            if (job is null) return Results.NotFound();
            if (job.Status is "Running" or "Stopping" or "Queued")
                return Results.Conflict(new { message = "运行中任务不能删除，请先停止。" });

            db.Jobs.Remove(job);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        group.MapGet("/{id:long}/download", async (long id, AppDbContext db) =>
        {
            var job = await db.Jobs.FindAsync(id);
            if (job?.Result is null)
                return Results.NotFound();

            var fullPath = Path.GetFullPath(job.Result);
            if (!File.Exists(fullPath))
                return Results.NotFound();

            return Results.File(
                new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite),
                "audio/mpeg",
                Path.GetFileName(fullPath),
                enableRangeProcessing: true);
        });

        return app;
    }

    private static async Task<IResult> RequeueAsync(
        NovelSystem.Domain.Entities.JobRecord job,
        AppDbContext db,
        JobQueue queue,
        bool isRetry)
    {
        if (string.IsNullOrWhiteSpace(job.Payload))
            return Results.BadRequest(new { message = "任务参数为空，无法继续。" });

        job.Status = "Queued";
        job.Error = null;
        job.FinishedAt = null;
        job.EstimatedCompletionAt = null;
        if (isRetry) job.RetryCount++;

        await db.SaveChangesAsync();
        queue.Enqueue(new JobMessage(job.Id, job.Type, job.Payload));
        return Results.Ok(job);
    }

    private static void NormalizePaging(ref int page, ref int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 5, 200);
    }
}