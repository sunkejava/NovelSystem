using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Application.Contracts;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Jobs;
using NovelSystem.Infrastructure.Persistence;
using NovelSystem.Infrastructure.Services;

namespace NovelSystem.Api.Endpoints;

/// <summary>
/// 专业有声书制作能力：章节、时间轴、发音词典、质量检测、FFprobe 时长和 A/B 音频版本。
/// </summary>
public static class ProductionEndpoints
{
    public static IEndpointRouteBuilder MapProductionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/production").WithTags("Production");

        group.MapGet("/novels/{novelId:long}/chapters", async (long novelId, AppDbContext db, CancellationToken ct) =>
        {
            var chapters = await db.NovelChapters.AsNoTracking()
                .Where(x => x.NovelId == novelId)
                .OrderBy(x => x.ChapterOrder)
                .Select(x => new
                {
                    x.Id,
                    x.VolumeTitle,
                    x.VolumeOrder,
                    x.ChapterOrder,
                    x.Title,
                    x.SourceStart,
                    x.SourceEnd,
                    scriptCount = db.ScriptLines.Count(s => s.ChapterId == x.Id)
                })
                .ToListAsync(ct);
            return Results.Ok(chapters);
        });

        group.MapPost("/novels/{novelId:long}/chapters/rebuild", async (long novelId, AppDbContext db, CancellationToken ct) =>
        {
            var count = await NovelStructureService.RebuildAsync(db, novelId, ct);
            return Results.Ok(new { chapters = count });
        });

        group.MapPost("/novels/{novelId:long}/timeline/recalculate", async (long novelId, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Novels.AnyAsync(x => x.Id == novelId, ct)) return Results.NotFound();
            await AudioTimelineService.RecalculateNovelTimelineAsync(db, novelId, ct);
            var totalDurationMs = await db.ScriptLines.AsNoTracking()
                .Where(x => x.NovelId == novelId && x.AudioEndMs != null)
                .MaxAsync(x => (long?)x.AudioEndMs, ct) ?? 0;
            var located = await db.ScriptLines.AsNoTracking()
                .CountAsync(x => x.NovelId == novelId && x.AudioStartMs != null && x.AudioEndMs != null, ct);
            return Results.Ok(new { located, totalDurationMs });
        });

        group.MapGet("/novels/{novelId:long}/timeline", async (
            long novelId,
            AppDbContext db,
            int page = 1,
            int pageSize = 50,
            long? chapterId = null,
            string? keyword = null,
            CancellationToken ct = default) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 10, 200);
            var query = db.ScriptLines.AsNoTracking().Where(x => x.NovelId == novelId);
            if (chapterId.HasValue) query = query.Where(x => x.ChapterId == chapterId.Value);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var value = keyword.Trim();
                query = query.Where(x => x.Text.Contains(value) || x.Speaker.Contains(value));
            }

            var total = await query.CountAsync(ct);
            var items = await query.OrderBy(x => x.Order)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.Id,
                    x.NovelId,
                    x.ChapterId,
                    x.CharacterId,
                    x.Order,
                    x.Speaker,
                    x.Text,
                    x.Emotion,
                    x.SourceStart,
                    x.SourceEnd,
                    x.AudioStartMs,
                    x.AudioEndMs,
                    durationMs = x.AudioStartMs != null && x.AudioEndMs != null ? x.AudioEndMs - x.AudioStartMs : null,
                    x.AudioFile,
                    x.Status,
                    chapterTitle = db.NovelChapters.Where(c => c.Id == x.ChapterId).Select(c => c.Title).FirstOrDefault(),
                    versionCount = db.ScriptAudioVersions.Count(v => v.ScriptLineId == x.Id)
                })
                .ToListAsync(ct);

            var totalDurationMs = await db.ScriptLines.AsNoTracking()
                .Where(x => x.NovelId == novelId && x.AudioEndMs != null)
                .MaxAsync(x => (long?)x.AudioEndMs, ct) ?? 0;

            return Results.Ok(new { items, total, page, pageSize, totalDurationMs });
        });

        group.MapPut("/timeline/{scriptId:long}", async (
            long scriptId,
            UpdateTimelineRequest request,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var line = await db.ScriptLines.FindAsync([scriptId], ct);
            if (line is null) return Results.NotFound();

            var contentChanged = !string.Equals(line.Text, request.Text, StringComparison.Ordinal) ||
                                 !string.Equals(line.Speaker, request.Speaker, StringComparison.Ordinal) ||
                                 !string.Equals(line.Emotion, request.Emotion, StringComparison.Ordinal);

            line.Speaker = string.IsNullOrWhiteSpace(request.Speaker) ? "旁白" : request.Speaker.Trim();
            line.Text = request.Text?.Trim() ?? string.Empty;
            line.Emotion = request.Emotion?.Trim();

            if (contentChanged)
            {
                line.AudioFile = null;
                line.Status = "Pending";
                line.AudioStartMs = null;
                line.AudioEndMs = null;
                var versions = await db.ScriptAudioVersions.Where(x => x.ScriptLineId == scriptId).ToListAsync(ct);
                foreach (var version in versions) version.IsSelected = false;
            }

            await db.SaveChangesAsync(ct);
            if (contentChanged) await AudioTimelineService.RecalculateNovelTimelineAsync(db, line.NovelId, ct);
            return Results.Ok(line);
        });

        group.MapGet("/timeline/{scriptId:long}/versions", async (long scriptId, AppDbContext db, CancellationToken ct) =>
        {
            var items = await db.ScriptAudioVersions.AsNoTracking()
                .Where(x => x.ScriptLineId == scriptId)
                .OrderByDescending(x => x.VersionNo)
                .ToListAsync(ct);
            return Results.Ok(items);
        });

        group.MapPost("/timeline/{scriptId:long}/versions/generate", async (
            long scriptId,
            AppDbContext db,
            ITtsClient tts,
            CancellationToken ct) =>
        {
            var line = await db.ScriptLines.FindAsync([scriptId], ct);
            if (line is null) return Results.NotFound();
            var voice = await ResolveVoiceAsync(db, line, ct);
            var nextVersion = (await db.ScriptAudioVersions.AsNoTracking()
                .Where(x => x.ScriptLineId == scriptId)
                .MaxAsync(x => (int?)x.VersionNo, ct) ?? 0) + 1;

            var output = $"storage/audio/{line.NovelId}/versions/{line.Id}-v{nextVersion:D2}.wav";
            await tts.GenerateAsync(line.Text, voice, output, ct);
            var duration = await AudioTimelineService.ProbeDurationMsAsync(db, output, ct) ?? 0;
            var version = new ScriptAudioVersion
            {
                NovelId = line.NovelId,
                ScriptLineId = line.Id,
                VersionNo = nextVersion,
                FilePath = output,
                DurationMs = duration,
                IsSelected = false,
                CreatedAt = DateTime.UtcNow
            };
            db.ScriptAudioVersions.Add(version);
            await db.SaveChangesAsync(ct);
            return Results.Ok(version);
        });

        group.MapPut("/timeline/versions/{versionId:long}/select", async (long versionId, AppDbContext db, CancellationToken ct) =>
        {
            var version = await db.ScriptAudioVersions.FindAsync([versionId], ct);
            if (version is null) return Results.NotFound();
            if (!File.Exists(Path.GetFullPath(version.FilePath)))
                return Results.BadRequest(new { message = "该音频版本文件不存在。" });

            var line = await db.ScriptLines.FindAsync([version.ScriptLineId], ct);
            if (line is null) return Results.NotFound();
            var versions = await db.ScriptAudioVersions.Where(x => x.ScriptLineId == line.Id).ToListAsync(ct);
            foreach (var item in versions) item.IsSelected = item.Id == version.Id;

            line.AudioFile = version.FilePath;
            line.Status = "Completed";
            await db.SaveChangesAsync(ct);
            await AudioTimelineService.RecalculateNovelTimelineAsync(db, line.NovelId, ct);
            return Results.Ok(new { scriptLineId = line.Id, versionId = version.Id, version.VersionNo, line.AudioStartMs, line.AudioEndMs });
        });

        group.MapDelete("/timeline/versions/{versionId:long}", async (long versionId, AppDbContext db, CancellationToken ct) =>
        {
            var version = await db.ScriptAudioVersions.FindAsync([versionId], ct);
            if (version is null) return Results.NotFound();
            if (version.IsSelected)
                return Results.BadRequest(new { message = "当前采用版本不能直接删除，请先选择其他版本。" });
            var path = Path.GetFullPath(version.FilePath);
            if (File.Exists(path)) File.Delete(path);
            db.ScriptAudioVersions.Remove(version);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapGet("/timeline/versions/{versionId:long}/play", async (long versionId, AppDbContext db, CancellationToken ct) =>
        {
            var version = await db.ScriptAudioVersions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == versionId, ct);
            if (version is null) return Results.NotFound();
            return PhysicalAudio(version.FilePath);
        });

        group.MapGet("/novels/{novelId:long}/pronunciations", async (long novelId, AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.PronunciationEntries.AsNoTracking().Where(x => x.NovelId == novelId).OrderBy(x => x.Pattern).ToListAsync(ct)));

        group.MapPost("/novels/{novelId:long}/pronunciations", async (long novelId, PronunciationRequest request, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Pattern) || string.IsNullOrWhiteSpace(request.Replacement))
                return Results.BadRequest(new { message = "原词和发音替换不能为空。" });
            var entity = new PronunciationEntry { NovelId = novelId, Pattern = request.Pattern.Trim(), Replacement = request.Replacement.Trim(), Note = request.Note?.Trim(), IsEnabled = request.IsEnabled, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            db.PronunciationEntries.Add(entity);
            await db.SaveChangesAsync(ct);
            return Results.Ok(entity);
        });

        group.MapPut("/pronunciations/{id:long}", async (long id, PronunciationRequest request, AppDbContext db, CancellationToken ct) =>
        {
            var entity = await db.PronunciationEntries.FindAsync([id], ct);
            if (entity is null) return Results.NotFound();
            entity.Pattern = request.Pattern.Trim(); entity.Replacement = request.Replacement.Trim(); entity.Note = request.Note?.Trim(); entity.IsEnabled = request.IsEnabled; entity.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(entity);
        });

        group.MapDelete("/pronunciations/{id:long}", async (long id, AppDbContext db, CancellationToken ct) =>
        {
            var entity = await db.PronunciationEntries.FindAsync([id], ct);
            if (entity is null) return Results.NotFound();
            db.PronunciationEntries.Remove(entity);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapGet("/novels/{novelId:long}/qa", async (long novelId, AppDbContext db, bool? resolved = null, string? severity = null, CancellationToken ct = default) =>
        {
            var query = db.NovelQaIssues.AsNoTracking().Where(x => x.NovelId == novelId);
            if (resolved.HasValue) query = query.Where(x => x.Resolved == resolved.Value);
            if (!string.IsNullOrWhiteSpace(severity)) query = query.Where(x => x.Severity == severity);
            return Results.Ok(await query.OrderByDescending(x => x.Severity == "Error").ThenBy(x => x.ScriptLineId).ThenBy(x => x.Id).ToListAsync(ct));
        });

        group.MapPost("/novels/{novelId:long}/qa/run", async (long novelId, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Novels.AnyAsync(x => x.Id == novelId, ct)) return Results.NotFound();
            var result = await RunQaAsync(db, novelId, ct);
            return Results.Ok(result);
        });

        group.MapPost("/novels/{novelId:long}/qa/auto-fix", async (long novelId, AppDbContext db, JobQueue queue, CancellationToken ct) =>
        {
            var issues = await db.NovelQaIssues.Where(x => x.NovelId == novelId && !x.Resolved).ToListAsync(ct);
            var rebuiltOffsets = false;
            var normalizedOrder = false;
            var queuedAudio = 0;

            if (issues.Any(x => x.Type == "SourceOffsetMissing"))
            {
                await NovelStructureService.RebuildAsync(db, novelId, ct);
                rebuiltOffsets = true;
            }

            if (issues.Any(x => x.Type == "OrderGap"))
            {
                var lines = await db.ScriptLines.Where(x => x.NovelId == novelId).OrderBy(x => x.Order).ToListAsync(ct);
                for (var i = 0; i < lines.Count; i++) lines[i].Order = i + 1;
                await db.SaveChangesAsync(ct);
                normalizedOrder = true;
            }

            var audioIds = issues.Where(x => x.ScriptLineId.HasValue && (x.Type == "AudioFailed" || x.Type == "AudioFileMissing")).Select(x => x.ScriptLineId!.Value).Distinct().ToList();
            foreach (var scriptId in audioIds)
            {
                var payload = JsonSerializer.Serialize(new { novelId, scriptLineId = scriptId });
                var job = new JobRecord { Type = "GenerateAudioSegment", Payload = payload };
                db.Jobs.Add(job);
                await db.SaveChangesAsync(ct);
                if (queue.Enqueue(new JobMessage(job.Id, job.Type, payload))) queuedAudio++;
            }

            await AudioTimelineService.RecalculateNovelTimelineAsync(db, novelId, ct);
            var remaining = await RunQaAsync(db, novelId, ct);
            return Results.Ok(new { rebuiltOffsets, normalizedOrder, queuedAudio, remaining });
        });

        group.MapPut("/qa/{id:long}/resolve", async (long id, bool resolved, AppDbContext db, CancellationToken ct) =>
        {
            var issue = await db.NovelQaIssues.FindAsync([id], ct);
            if (issue is null) return Results.NotFound();
            issue.Resolved = resolved;
            await db.SaveChangesAsync(ct);
            return Results.Ok(issue);
        });

        return app;
    }

    private static async Task<object> RunQaAsync(AppDbContext db, long novelId, CancellationToken ct)
    {
        var novel = await db.Novels.AsNoTracking().FirstAsync(x => x.Id == novelId, ct);
        var previous = await db.NovelQaIssues.Where(x => x.NovelId == novelId && !x.Resolved).ToListAsync(ct);
        db.NovelQaIssues.RemoveRange(previous);
        var issues = new List<NovelQaIssue>();
        var scripts = await db.ScriptLines.AsNoTracking().Where(x => x.NovelId == novelId).OrderBy(x => x.Order).ToListAsync(ct);
        var characterVoices = await db.Characters.AsNoTracking().Where(x => x.NovelId == novelId).ToDictionaryAsync(x => x.Id, x => x.VoiceProfileId, ct);
        if (scripts.Any(x => string.Equals(x.Speaker, "旁白", StringComparison.OrdinalIgnoreCase)) && !novel.NarratorVoiceProfileId.HasValue) issues.Add(NewIssue(novelId, null, "NarratorVoiceMissing", "Error", "小说包含旁白脚本，但尚未绑定旁白音色。"));
        for (var i = 0; i < scripts.Count; i++)
        {
            var line = scripts[i];
            if (string.IsNullOrWhiteSpace(line.Text)) issues.Add(NewIssue(novelId, line.Id, "EmptyText", "Error", $"脚本 #{line.Order} 文本为空。"));
            if (line.SourceStart < 0 || line.SourceEnd <= line.SourceStart) issues.Add(NewIssue(novelId, line.Id, "SourceOffsetMissing", "Warning", $"脚本 #{line.Order} 无法精确定位到小说原文。"));
            if (line.Text.Length > 450) issues.Add(NewIssue(novelId, line.Id, "ScriptTooLong", "Warning", $"脚本 #{line.Order} 长度 {line.Text.Length} 字，建议拆分以提升 TTS 稳定性。"));
            if (line.Status == "Failed") issues.Add(NewIssue(novelId, line.Id, "AudioFailed", "Error", $"脚本 #{line.Order} 音频生成失败。"));
            if (line.Status == "Completed" && (string.IsNullOrWhiteSpace(line.AudioFile) || !File.Exists(Path.GetFullPath(line.AudioFile)))) issues.Add(NewIssue(novelId, line.Id, "AudioFileMissing", "Error", $"脚本 #{line.Order} 标记为已完成，但音频文件不存在。"));
            if (line.Status == "Completed" && (!line.AudioStartMs.HasValue || !line.AudioEndMs.HasValue)) issues.Add(NewIssue(novelId, line.Id, "AudioTimelineMissing", "Warning", $"脚本 #{line.Order} 已有音频，但尚未写入精确时间轴。"));
            if (line.CharacterId.HasValue && (!characterVoices.TryGetValue(line.CharacterId.Value, out var voiceId) || !voiceId.HasValue)) issues.Add(NewIssue(novelId, line.Id, "CharacterVoiceMissing", "Error", $"脚本 #{line.Order} 的角色“{line.Speaker}”尚未绑定音色。"));
            if (i > 0 && string.Equals(scripts[i - 1].Text.Trim(), line.Text.Trim(), StringComparison.Ordinal)) issues.Add(NewIssue(novelId, line.Id, "DuplicateAdjacentText", "Warning", $"脚本 #{line.Order} 与上一条文本完全重复。"));
            if (i > 0 && line.Order != scripts[i - 1].Order + 1) issues.Add(NewIssue(novelId, line.Id, "OrderGap", "Warning", $"脚本顺序存在跳号：{scripts[i - 1].Order} → {line.Order}。"));
        }
        db.NovelQaIssues.AddRange(issues);
        await db.SaveChangesAsync(ct);
        return new { total = issues.Count, errors = issues.Count(x => x.Severity == "Error"), warnings = issues.Count(x => x.Severity == "Warning") };
    }

    private static async Task<VoiceProfile> ResolveVoiceAsync(AppDbContext db, ScriptLine line, CancellationToken ct)
    {
        long? voiceId;
        if (line.CharacterId is null || string.Equals(line.Speaker, "旁白", StringComparison.OrdinalIgnoreCase))
            voiceId = await db.Novels.AsNoTracking().Where(x => x.Id == line.NovelId).Select(x => x.NarratorVoiceProfileId).SingleOrDefaultAsync(ct);
        else
            voiceId = await db.Characters.AsNoTracking().Where(x => x.Id == line.CharacterId.Value).Select(x => x.VoiceProfileId).SingleOrDefaultAsync(ct);
        if (!voiceId.HasValue) throw new InvalidOperationException($"角色“{line.Speaker}”尚未绑定音色。");
        return await db.VoiceProfiles.FindAsync([voiceId.Value], ct) ?? throw new InvalidOperationException("绑定的音色档案不存在。");
    }

    private static IResult PhysicalAudio(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) return Results.NotFound(new { message = "音频文件不存在。" });
        var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Results.File(stream, "audio/wav", enableRangeProcessing: true);
    }

    private static NovelQaIssue NewIssue(long novelId, long? scriptLineId, string type, string severity, string message)
        => new() { NovelId = novelId, ScriptLineId = scriptLineId, Type = type, Severity = severity, Message = message, Resolved = false, CreatedAt = DateTime.UtcNow };

    public sealed record UpdateTimelineRequest(string Speaker, string Text, string? Emotion);
    public sealed record PronunciationRequest(string Pattern, string Replacement, string? Note, bool IsEnabled = true);
}
