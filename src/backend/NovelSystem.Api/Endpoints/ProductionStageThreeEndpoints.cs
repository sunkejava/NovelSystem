using Microsoft.EntityFrameworkCore;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Persistence;
using NovelSystem.Infrastructure.Services;

namespace NovelSystem.Api.Endpoints;

/// <summary>第三阶段专业制作：读听同步、多轨 BGM/SFX 编辑与混音导出。</summary>
public static class ProductionStageThreeEndpoints
{
    private static readonly HashSet<string> AllowedAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".m4a", ".aac", ".flac", ".ogg"
    };

    public static IEndpointRouteBuilder MapProductionStageThreeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/production").WithTags("Production Stage 3");

        group.MapGet("/novels/{novelId:long}/sync/chapters", async (long novelId, AppDbContext db, CancellationToken ct) =>
        {
            var chapters = await db.NovelChapters.AsNoTracking()
                .Where(x => x.NovelId == novelId)
                .OrderBy(x => x.ChapterOrder)
                .Select(x => new
                {
                    x.Id,
                    x.ChapterOrder,
                    x.Title,
                    x.SourceStart,
                    x.SourceEnd,
                    audioStartMs = db.ScriptLines.Where(s => s.ChapterId == x.Id && s.AudioStartMs != null).Min(s => (long?)s.AudioStartMs),
                    audioEndMs = db.ScriptLines.Where(s => s.ChapterId == x.Id && s.AudioEndMs != null).Max(s => (long?)s.AudioEndMs),
                    scriptCount = db.ScriptLines.Count(s => s.ChapterId == x.Id)
                })
                .ToListAsync(ct);
            return Results.Ok(chapters);
        });

        group.MapGet("/novels/{novelId:long}/sync/chapter/{chapterId:long}", async (
            long novelId,
            long chapterId,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var novel = await db.Novels.AsNoTracking()
                .Where(x => x.Id == novelId)
                .Select(x => new { x.Id, x.Title, x.Content })
                .FirstOrDefaultAsync(ct);
            if (novel is null) return Results.NotFound();

            var chapter = await db.NovelChapters.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == chapterId && x.NovelId == novelId, ct);
            if (chapter is null) return Results.NotFound();

            var source = NormalizeSource(novel.Content);
            var start = Math.Clamp(chapter.SourceStart, 0, source.Length);
            var end = Math.Clamp(chapter.SourceEnd, start, source.Length);
            var chapterText = source[start..end];

            var scripts = await db.ScriptLines.AsNoTracking()
                .Where(x => x.NovelId == novelId && x.ChapterId == chapterId)
                .OrderBy(x => x.Order)
                .Select(x => new
                {
                    x.Id,
                    x.Order,
                    x.Speaker,
                    x.Text,
                    x.Emotion,
                    x.SourceStart,
                    x.SourceEnd,
                    localSourceStart = x.SourceStart >= 0 ? x.SourceStart - chapter.SourceStart : -1,
                    localSourceEnd = x.SourceEnd >= 0 ? x.SourceEnd - chapter.SourceStart : -1,
                    x.AudioStartMs,
                    x.AudioEndMs,
                    x.Status
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                novelId,
                novel.Title,
                chapter = new { chapter.Id, chapter.ChapterOrder, chapter.Title, chapter.SourceStart, chapter.SourceEnd },
                sourceText = chapterText,
                scripts,
                mergedExists = File.Exists(Path.GetFullPath($"storage/output/novel-{novelId}.mp3"))
            });
        });

        group.MapGet("/novels/{novelId:long}/tracks", async (long novelId, AppDbContext db, CancellationToken ct) =>
        {
            var tracks = await db.ProductionTracks.AsNoTracking()
                .Where(x => x.NovelId == novelId)
                .OrderBy(x => x.Order)
                .ToListAsync(ct);
            var ids = tracks.Select(x => x.Id).ToList();
            var clips = ids.Count == 0
                ? []
                : await db.ProductionTrackClips.AsNoTracking()
                    .Where(x => ids.Contains(x.TrackId))
                    .OrderBy(x => x.StartMs)
                    .ToListAsync(ct);

            var voiceDuration = await db.ScriptLines.AsNoTracking()
                .Where(x => x.NovelId == novelId && x.AudioEndMs != null)
                .MaxAsync(x => (long?)x.AudioEndMs, ct) ?? 0;

            return Results.Ok(new
            {
                voiceTrack = new { name = "对白主轨", type = "Voice", startMs = 0L, endMs = voiceDuration },
                tracks = tracks.Select(t => new
                {
                    t.Id, t.Name, t.Type, t.Order, t.Volume, t.IsMuted,
                    clips = clips.Where(c => c.TrackId == t.Id).Select(c => new
                    {
                        c.Id, c.Name, c.FilePath, c.StartMs, c.EndMs, c.DurationMs, c.Volume, c.FadeInMs, c.FadeOutMs
                    })
                }),
                mixedExists = File.Exists(Path.GetFullPath($"storage/output/novel-{novelId}-mixed.mp3"))
            });
        });

        group.MapPost("/novels/{novelId:long}/tracks/defaults", async (long novelId, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Novels.AnyAsync(x => x.Id == novelId, ct)) return Results.NotFound();
            var existing = await db.ProductionTracks.Where(x => x.NovelId == novelId).ToListAsync(ct);
            if (!existing.Any(x => x.Type == "Bgm"))
                db.ProductionTracks.Add(new ProductionTrack { NovelId = novelId, Name = "背景音乐", Type = "Bgm", Order = 10 });
            if (!existing.Any(x => x.Type == "Sfx"))
                db.ProductionTracks.Add(new ProductionTrack { NovelId = novelId, Name = "环境与音效", Type = "Sfx", Order = 20 });
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        group.MapPost("/novels/{novelId:long}/tracks", async (long novelId, TrackRequest request, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Novels.AnyAsync(x => x.Id == novelId, ct)) return Results.NotFound();
            var type = NormalizeTrackType(request.Type);
            var order = (await db.ProductionTracks.AsNoTracking().Where(x => x.NovelId == novelId).MaxAsync(x => (int?)x.Order, ct) ?? 0) + 10;
            var track = new ProductionTrack
            {
                NovelId = novelId,
                Name = string.IsNullOrWhiteSpace(request.Name) ? (type == "Bgm" ? "背景音乐" : "音效") : request.Name.Trim(),
                Type = type,
                Order = order,
                Volume = Math.Clamp(request.Volume, 0d, 4d),
                IsMuted = request.IsMuted
            };
            db.ProductionTracks.Add(track);
            await db.SaveChangesAsync(ct);
            return Results.Ok(track);
        });

        group.MapPut("/tracks/{trackId:long}", async (long trackId, TrackRequest request, AppDbContext db, CancellationToken ct) =>
        {
            var track = await db.ProductionTracks.FindAsync([trackId], ct);
            if (track is null) return Results.NotFound();
            track.Name = string.IsNullOrWhiteSpace(request.Name) ? track.Name : request.Name.Trim();
            track.Type = NormalizeTrackType(request.Type);
            track.Volume = Math.Clamp(request.Volume, 0d, 4d);
            track.IsMuted = request.IsMuted;
            track.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(track);
        });

        group.MapDelete("/tracks/{trackId:long}", async (long trackId, AppDbContext db, CancellationToken ct) =>
        {
            var track = await db.ProductionTracks.FindAsync([trackId], ct);
            if (track is null) return Results.NotFound();
            var clips = await db.ProductionTrackClips.Where(x => x.TrackId == trackId).ToListAsync(ct);
            foreach (var clip in clips) SafeDelete(clip.FilePath);
            db.ProductionTrackClips.RemoveRange(clips);
            db.ProductionTracks.Remove(track);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/tracks/{trackId:long}/clips/upload", async (
            long trackId,
            HttpRequest httpRequest,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var track = await db.ProductionTracks.FindAsync([trackId], ct);
            if (track is null) return Results.NotFound();
            if (!httpRequest.HasFormContentType) return Results.BadRequest(new { message = "请使用 multipart/form-data 上传音频。" });
            var form = await httpRequest.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return Results.BadRequest(new { message = "请选择音频文件。" });
            var extension = Path.GetExtension(file.FileName);
            if (!AllowedAudioExtensions.Contains(extension))
                return Results.BadRequest(new { message = "仅支持 wav/mp3/m4a/aac/flac/ogg 音频。" });

            var directory = Path.GetFullPath($"storage/production/{track.NovelId}/tracks/{track.Id}");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
                await file.CopyToAsync(stream, ct);

            var duration = await AudioTimelineService.ProbeDurationMsAsync(db, path, ct);
            if (!duration.HasValue || duration.Value <= 0)
            {
                SafeDelete(path);
                return Results.BadRequest(new { message = "无法解析音频时长，请检查 FFprobe 配置或文件格式。" });
            }

            var startMs = ParseLong(form["startMs"], 0);
            var clip = new ProductionTrackClip
            {
                NovelId = track.NovelId,
                TrackId = track.Id,
                Name = string.IsNullOrWhiteSpace(form["name"]) ? Path.GetFileNameWithoutExtension(file.FileName) : form["name"].ToString().Trim(),
                FilePath = path,
                StartMs = Math.Max(0, startMs),
                DurationMs = duration.Value,
                EndMs = Math.Max(0, startMs) + duration.Value,
                Volume = Math.Clamp(ParseDouble(form["volume"], 1d), 0d, 4d),
                FadeInMs = Math.Max(0, (int)ParseLong(form["fadeInMs"], 0)),
                FadeOutMs = Math.Max(0, (int)ParseLong(form["fadeOutMs"], 0))
            };
            db.ProductionTrackClips.Add(clip);
            await db.SaveChangesAsync(ct);
            return Results.Ok(clip);
        });

        group.MapPut("/tracks/clips/{clipId:long}", async (long clipId, ClipRequest request, AppDbContext db, CancellationToken ct) =>
        {
            var clip = await db.ProductionTrackClips.FindAsync([clipId], ct);
            if (clip is null) return Results.NotFound();
            clip.Name = string.IsNullOrWhiteSpace(request.Name) ? clip.Name : request.Name.Trim();
            clip.StartMs = Math.Max(0, request.StartMs);
            clip.EndMs = clip.StartMs + clip.DurationMs;
            clip.Volume = Math.Clamp(request.Volume, 0d, 4d);
            clip.FadeInMs = Math.Clamp(request.FadeInMs, 0, (int)Math.Min(int.MaxValue, clip.DurationMs));
            clip.FadeOutMs = Math.Clamp(request.FadeOutMs, 0, (int)Math.Min(int.MaxValue, clip.DurationMs));
            clip.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(clip);
        });

        group.MapDelete("/tracks/clips/{clipId:long}", async (long clipId, AppDbContext db, CancellationToken ct) =>
        {
            var clip = await db.ProductionTrackClips.FindAsync([clipId], ct);
            if (clip is null) return Results.NotFound();
            SafeDelete(clip.FilePath);
            db.ProductionTrackClips.Remove(clip);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapGet("/tracks/clips/{clipId:long}/play", async (long clipId, AppDbContext db, CancellationToken ct) =>
        {
            var clip = await db.ProductionTrackClips.AsNoTracking().FirstOrDefaultAsync(x => x.Id == clipId, ct);
            return clip is null ? Results.NotFound() : PhysicalAudio(clip.FilePath);
        });

        group.MapPost("/novels/{novelId:long}/tracks/mix", async (long novelId, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Novels.AnyAsync(x => x.Id == novelId, ct)) return Results.NotFound();
            var output = await ProductionMixService.MixAsync(db, novelId, ct);
            return Results.Ok(new { output });
        });

        group.MapGet("/novels/{novelId:long}/tracks/mixed/play", (long novelId) =>
            PhysicalAudio($"storage/output/novel-{novelId}-mixed.mp3"));

        group.MapGet("/novels/{novelId:long}/tracks/mixed/download", async (long novelId, AppDbContext db, CancellationToken ct) =>
        {
            var title = await db.Novels.AsNoTracking().Where(x => x.Id == novelId).Select(x => x.Title).FirstOrDefaultAsync(ct);
            if (title is null) return Results.NotFound();
            var path = Path.GetFullPath($"storage/output/novel-{novelId}-mixed.mp3");
            if (!File.Exists(path)) return Results.NotFound();
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, true);
            return Results.File(stream, "audio/mpeg", SanitizeFileName(title) + "-混音版.mp3", enableRangeProcessing: true);
        });

        return app;
    }

    private static string NormalizeTrackType(string? type)
        => string.Equals(type, "Sfx", StringComparison.OrdinalIgnoreCase) ? "Sfx" : "Bgm";

    private static string NormalizeSource(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    private static long ParseLong(Microsoft.Extensions.Primitives.StringValues value, long fallback)
        => long.TryParse(value.ToString(), out var result) ? result : fallback;

    private static double ParseDouble(Microsoft.Extensions.Primitives.StringValues value, double fallback)
        => double.TryParse(value.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : fallback;

    private static void SafeDelete(string path)
    {
        try { var full = Path.GetFullPath(path); if (File.Exists(full)) File.Delete(full); } catch { }
    }

    private static IResult PhysicalAudio(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        if (!File.Exists(full)) return Results.NotFound(new { message = "音频文件不存在。" });
        var extension = Path.GetExtension(full).ToLowerInvariant();
        var contentType = extension switch
        {
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".m4a" => "audio/mp4",
            ".aac" => "audio/aac",
            ".flac" => "audio/flac",
            _ => "audio/mpeg"
        };
        var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, true);
        return Results.File(stream, contentType, enableRangeProcessing: true);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return value;
    }

    public sealed record TrackRequest(string Name, string Type = "Bgm", double Volume = 1d, bool IsMuted = false);
    public sealed record ClipRequest(string Name, long StartMs, double Volume = 1d, int FadeInMs = 0, int FadeOutMs = 0);
}
