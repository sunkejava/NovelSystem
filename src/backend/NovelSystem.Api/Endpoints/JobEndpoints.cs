using Microsoft.EntityFrameworkCore;
using NovelSystem.Infrastructure.Jobs;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Api.Endpoints;

/// <summary>后台任务查询、停止、继续、失败重试、删除和结果下载 API。</summary>
public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/jobs").WithTags("Jobs");

        group.MapGet("/", async (AppDbContext db) =>
            await db.Jobs.OrderByDescending(x => x.Id).Take(200).ToListAsync());

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
            return job?.Result is null || !File.Exists(job.Result)
                ? Results.NotFound()
                : Results.File(job.Result, "audio/mpeg", Path.GetFileName(job.Result));
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
        if (isRetry) job.RetryCount++;

        await db.SaveChangesAsync();
        queue.Enqueue(new JobMessage(job.Id, job.Type, job.Payload));
        return Results.Ok(job);
    }
}