using Microsoft.EntityFrameworkCore;
using NovelSystem.Infrastructure.Jobs;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Api.Endpoints;

/// <summary>LLM 结构化解析异常诊断记录。</summary>
public static class AnalysisDiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalysisDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analysis-errors").WithTags("AnalysisDiagnostics");

        group.MapGet("/", async (
            AppDbContext db,
            int page = 1,
            int pageSize = 20,
            long? novelId = null,
            long? jobId = null,
            bool? recovered = null) =>
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 5, 100);
            var query = db.AiAnalysisErrors.AsNoTracking();

            if (novelId.HasValue) query = query.Where(x => x.NovelId == novelId);
            if (jobId.HasValue) query = query.Where(x => x.JobId == jobId);
            if (recovered.HasValue) query = query.Where(x => x.Recovered == recovered);

            var total = await query.CountAsync();
            var items = await query.OrderByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.Id,
                    x.NovelId,
                    x.JobId,
                    x.ChunkIndex,
                    x.ChunkTotal,
                    x.RetryDepth,
                    x.Stage,
                    x.SourceText,
                    x.RawResponse,
                    x.Error,
                    x.Recovered,
                    createdAt = JobTimingCalculator.ToUtcIso(x.CreatedAt)
                })
                .ToListAsync();

            return Results.Ok(new { items, total, page, pageSize });
        });

        return app;
    }
}