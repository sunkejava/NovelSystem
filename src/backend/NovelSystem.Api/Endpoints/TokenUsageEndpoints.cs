using Microsoft.EntityFrameworkCore;
using NovelSystem.Infrastructure.Jobs;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Api.Endpoints;

/// <summary>
/// 本地 AI Token 与性能统计 API。
/// 支持按小说、任务、操作类型和时间范围查看汇总及单次调用明细。
/// </summary>
public static class TokenUsageEndpoints
{
    public static IEndpointRouteBuilder MapTokenUsageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/token-usage").WithTags("TokenUsage");

        group.MapGet("/", async (
            AppDbContext db,
            int page = 1,
            int pageSize = 20,
            long? novelId = null,
            long? jobId = null,
            string? operation = null,
            DateTime? from = null,
            DateTime? to = null) =>
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 5, 200);

            var query = ApplyFilters(
                db.AiTokenUsages.AsNoTracking(),
                novelId,
                jobId,
                operation,
                from,
                to);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.Id,
                    x.NovelId,
                    NovelTitle = x.NovelId == null
                        ? null
                        : db.Novels.Where(n => n.Id == x.NovelId).Select(n => n.Title).FirstOrDefault(),
                    x.JobId,
                    x.Operation,
                    x.ChunkIndex,
                    x.ChunkTotal,
                    x.Model,
                    x.PromptTokens,
                    x.CompletionTokens,
                    x.TotalTokens,
                    x.CachedPromptTokens,
                    x.ElapsedMilliseconds,
                    x.PromptTokensPerSecond,
                    x.CompletionTokensPerSecond,
                    x.InputCharacters,
                    x.OutputCharacters,
                    x.IsEstimated,
                    x.Success,
                    x.Error,
                    CreatedAt = JobTimingCalculator.ToUtcIso(x.CreatedAt)
                })
                .ToListAsync();

            return Results.Ok(new { items, total, page, pageSize });
        });

        group.MapGet("/summary", async (
            AppDbContext db,
            long? novelId = null,
            long? jobId = null,
            string? operation = null,
            DateTime? from = null,
            DateTime? to = null) =>
        {
            var query = ApplyFilters(
                db.AiTokenUsages.AsNoTracking(),
                novelId,
                jobId,
                operation,
                from,
                to);

            var totals = await query
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Calls = g.Count(),
                    SuccessCalls = g.Count(x => x.Success),
                    FailedCalls = g.Count(x => !x.Success),
                    PromptTokens = g.Sum(x => x.PromptTokens),
                    CompletionTokens = g.Sum(x => x.CompletionTokens),
                    TotalTokens = g.Sum(x => x.TotalTokens),
                    CachedPromptTokens = g.Sum(x => x.CachedPromptTokens),
                    ElapsedMilliseconds = g.Sum(x => x.ElapsedMilliseconds),
                    AverageElapsedMilliseconds = g.Average(x => (double)x.ElapsedMilliseconds),
                    AverageCompletionTokensPerSecond = g.Average(x => x.CompletionTokensPerSecond),
                    EstimatedCalls = g.Count(x => x.IsEstimated)
                })
                .FirstOrDefaultAsync();

            var byOperation = await query
                .GroupBy(x => x.Operation)
                .Select(g => new
                {
                    Operation = g.Key,
                    Calls = g.Count(),
                    PromptTokens = g.Sum(x => x.PromptTokens),
                    CompletionTokens = g.Sum(x => x.CompletionTokens),
                    TotalTokens = g.Sum(x => x.TotalTokens),
                    ElapsedMilliseconds = g.Sum(x => x.ElapsedMilliseconds),
                    AverageCompletionTokensPerSecond = g.Average(x => x.CompletionTokensPerSecond),
                    EstimatedCalls = g.Count(x => x.IsEstimated)
                })
                .OrderByDescending(x => x.TotalTokens)
                .ToListAsync();

            var novelGroups = await query
                .Where(x => x.NovelId != null)
                .GroupBy(x => x.NovelId!.Value)
                .Select(g => new
                {
                    NovelId = g.Key,
                    Calls = g.Count(),
                    PromptTokens = g.Sum(x => x.PromptTokens),
                    CompletionTokens = g.Sum(x => x.CompletionTokens),
                    TotalTokens = g.Sum(x => x.TotalTokens),
                    ElapsedMilliseconds = g.Sum(x => x.ElapsedMilliseconds)
                })
                .OrderByDescending(x => x.TotalTokens)
                .Take(100)
                .ToListAsync();

            var novelIds = novelGroups.Select(x => x.NovelId).ToList();
            var novelTitles = await db.Novels.AsNoTracking()
                .Where(x => novelIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Title);

            var byNovel = novelGroups.Select(x => new
            {
                x.NovelId,
                NovelTitle = novelTitles.TryGetValue(x.NovelId, out var title) ? title : $"Novel #{x.NovelId}",
                x.Calls,
                x.PromptTokens,
                x.CompletionTokens,
                x.TotalTokens,
                x.ElapsedMilliseconds
            });

            var operations = await db.AiTokenUsages.AsNoTracking()
                .Select(x => x.Operation)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            return Results.Ok(new
            {
                totals = totals ?? new
                {
                    Calls = 0,
                    SuccessCalls = 0,
                    FailedCalls = 0,
                    PromptTokens = 0,
                    CompletionTokens = 0,
                    TotalTokens = 0,
                    CachedPromptTokens = 0,
                    ElapsedMilliseconds = 0L,
                    AverageElapsedMilliseconds = 0d,
                    AverageCompletionTokensPerSecond = 0d,
                    EstimatedCalls = 0
                },
                byOperation,
                byNovel,
                operations
            });
        });

        return app;
    }

    private static IQueryable<NovelSystem.Domain.Entities.AiTokenUsage> ApplyFilters(
        IQueryable<NovelSystem.Domain.Entities.AiTokenUsage> query,
        long? novelId,
        long? jobId,
        string? operation,
        DateTime? from,
        DateTime? to)
    {
        if (novelId.HasValue)
            query = query.Where(x => x.NovelId == novelId.Value);

        if (jobId.HasValue)
            query = query.Where(x => x.JobId == jobId.Value);

        if (!string.IsNullOrWhiteSpace(operation))
            query = query.Where(x => x.Operation == operation);

        if (from.HasValue)
            query = query.Where(x => x.CreatedAt >= from.Value.ToUniversalTime());

        if (to.HasValue)
            query = query.Where(x => x.CreatedAt <= to.Value.ToUniversalTime());

        return query;
    }
}