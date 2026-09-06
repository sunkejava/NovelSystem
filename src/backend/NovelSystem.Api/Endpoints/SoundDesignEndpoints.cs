using Microsoft.EntityFrameworkCore;
using NovelSystem.Application.Contracts;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Persistence;
using NovelSystem.Infrastructure.Services;

namespace NovelSystem.Api.Endpoints;

/// <summary>AI 声音导演：章节场景分析、Cue 审核和应用到多轨编排。</summary>
public static class SoundDesignEndpoints
{
    public static IEndpointRouteBuilder MapSoundDesignEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/production").WithTags("AI Sound Director");

        group.MapGet("/novels/{novelId:long}/sound-design", async (
            long novelId,
            AppDbContext db,
            long? chapterId = null,
            bool? approved = null,
            bool? applied = null,
            CancellationToken ct = default) =>
        {
            var query = db.SoundDesignCues.AsNoTracking().Where(x => x.NovelId == novelId);
            if (chapterId.HasValue) query = query.Where(x => x.ChapterId == chapterId.Value);
            if (approved.HasValue) query = query.Where(x => x.Approved == approved.Value);
            if (applied.HasValue) query = query.Where(x => x.Applied == applied.Value);

            var items = await query.OrderBy(x => x.StartMs).ThenBy(x => x.Type)
                .Select(x => new
                {
                    x.Id, x.NovelId, x.ChapterId, x.ScriptLineId, x.Type, x.Scene, x.Mood,
                    x.Intensity, x.Keywords, x.Description, x.StartMs, x.EndMs,
                    x.Approved, x.Applied, x.CreatedAt,
                    chapterTitle = db.NovelChapters.Where(c => c.Id == x.ChapterId).Select(c => c.Title).FirstOrDefault(),
                    scriptOrder = db.ScriptLines.Where(s => s.Id == x.ScriptLineId).Select(s => (int?)s.Order).FirstOrDefault()
                })
                .ToListAsync(ct);
            return Results.Ok(items);
        });

        group.MapPost("/novels/{novelId:long}/sound-design/analyze/{chapterId:long}", async (
            long novelId,
            long chapterId,
            AppDbContext db,
            IAiChatClient ai,
            CancellationToken ct) =>
        {
            var old = await db.SoundDesignCues
                .Where(x => x.NovelId == novelId && x.ChapterId == chapterId && !x.Approved)
                .ToListAsync(ct);
            if (old.Count > 0)
            {
                db.SoundDesignCues.RemoveRange(old);
                await db.SaveChangesAsync(ct);
            }

            var count = await SoundDesignService.AnalyzeChapterAsync(
                db, ai, novelId, chapterId, null, null, null, ct);
            return Results.Ok(new { created = count });
        });

        group.MapPut("/sound-design/{id:long}", async (
            long id,
            SoundDesignCueRequest request,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var cue = await db.SoundDesignCues.FindAsync([id], ct);
            if (cue is null) return Results.NotFound();
            cue.Type = string.Equals(request.Type, "Bgm", StringComparison.OrdinalIgnoreCase) ? "Bgm" : "Sfx";
            cue.Scene = request.Scene?.Trim() ?? cue.Scene;
            cue.Mood = request.Mood?.Trim() ?? cue.Mood;
            cue.Intensity = Math.Clamp(request.Intensity, 0, 100);
            cue.Keywords = request.Keywords?.Trim() ?? cue.Keywords;
            cue.Description = request.Description?.Trim() ?? cue.Description;
            cue.StartMs = Math.Max(0, request.StartMs);
            cue.EndMs = Math.Max(cue.StartMs, request.EndMs);
            cue.Approved = request.Approved;
            cue.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(cue);
        });

        group.MapPut("/sound-design/{id:long}/approve", async (long id, bool approved, AppDbContext db, CancellationToken ct) =>
        {
            var cue = await db.SoundDesignCues.FindAsync([id], ct);
            if (cue is null) return Results.NotFound();
            cue.Approved = approved;
            if (!approved) cue.Applied = false;
            cue.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(cue);
        });

        group.MapPost("/novels/{novelId:long}/sound-design/approve-all", async (long novelId, AppDbContext db, long? chapterId = null, CancellationToken ct = default) =>
        {
            var query = db.SoundDesignCues.Where(x => x.NovelId == novelId);
            if (chapterId.HasValue) query = query.Where(x => x.ChapterId == chapterId.Value);
            var items = await query.ToListAsync(ct);
            foreach (var cue in items)
            {
                cue.Approved = true;
                cue.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { approved = items.Count });
        });

        group.MapPost("/novels/{novelId:long}/sound-design/apply", async (long novelId, AppDbContext db, long? chapterId = null, CancellationToken ct = default) =>
        {
            await EnsureDefaultTracksAsync(db, novelId, ct);
            var query = db.SoundDesignCues.Where(x => x.NovelId == novelId && x.Approved);
            if (chapterId.HasValue) query = query.Where(x => x.ChapterId == chapterId.Value);
            var items = await query.ToListAsync(ct);
            foreach (var cue in items)
            {
                cue.Applied = true;
                cue.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { applied = items.Count });
        });

        group.MapDelete("/sound-design/{id:long}", async (long id, AppDbContext db, CancellationToken ct) =>
        {
            var cue = await db.SoundDesignCues.FindAsync([id], ct);
            if (cue is null) return Results.NotFound();
            db.SoundDesignCues.Remove(cue);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }

    private static async Task EnsureDefaultTracksAsync(AppDbContext db, long novelId, CancellationToken ct)
    {
        var tracks = await db.ProductionTracks.Where(x => x.NovelId == novelId).ToListAsync(ct);
        if (!tracks.Any(x => x.Type == "Bgm"))
            db.ProductionTracks.Add(new ProductionTrack { NovelId = novelId, Name = "背景音乐", Type = "Bgm", Order = 10 });
        if (!tracks.Any(x => x.Type == "Sfx"))
            db.ProductionTracks.Add(new ProductionTrack { NovelId = novelId, Name = "环境与音效", Type = "Sfx", Order = 20 });
        await db.SaveChangesAsync(ct);
    }

    public sealed record SoundDesignCueRequest(
        string? Type,
        string? Scene,
        string? Mood,
        int Intensity,
        string? Keywords,
        string? Description,
        long StartMs,
        long EndMs,
        bool Approved);
}
