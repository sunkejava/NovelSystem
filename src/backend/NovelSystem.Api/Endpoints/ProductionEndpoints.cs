using Microsoft.EntityFrameworkCore;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Persistence;
using NovelSystem.Infrastructure.Services;

namespace NovelSystem.Api.Endpoints;

/// <summary>
/// 专业有声书第一阶段制作能力：章节、时间轴、发音词典与质量检测。
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
            if (chapterId.HasValue)
                query = query.Where(x => x.ChapterId == chapterId.Value);
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
                    x.AudioFile,
                    x.Status,
                    chapterTitle = db.NovelChapters.Where(c => c.Id == x.ChapterId).Select(c => c.Title).FirstOrDefault()
                })
                .ToListAsync(ct);

            return Results.Ok(new { items, total, page, pageSize });
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
                if (!string.IsNullOrWhiteSpace(line.AudioFile) && File.Exists(line.AudioFile))
                    File.Delete(line.AudioFile);
                line.AudioFile = null;
                line.Status = "Pending";
                line.AudioStartMs = null;
                line.AudioEndMs = null;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(line);
        });

        group.MapGet("/novels/{novelId:long}/pronunciations", async (long novelId, AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.PronunciationEntries.AsNoTracking()
                .Where(x => x.NovelId == novelId)
                .OrderBy(x => x.Pattern)
                .ToListAsync(ct)));

        group.MapPost("/novels/{novelId:long}/pronunciations", async (
            long novelId,
            PronunciationRequest request,
            AppDbContext db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Pattern) || string.IsNullOrWhiteSpace(request.Replacement))
                return Results.BadRequest(new { message = "原词和发音替换不能为空。" });

            var entity = new PronunciationEntry
            {
                NovelId = novelId,
                Pattern = request.Pattern.Trim(),
                Replacement = request.Replacement.Trim(),
                Note = request.Note?.Trim(),
                IsEnabled = request.IsEnabled,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.PronunciationEntries.Add(entity);
            await db.SaveChangesAsync(ct);
            return Results.Ok(entity);
        });

        group.MapPut("/pronunciations/{id:long}", async (
            long id,
            PronunciationRequest request,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var entity = await db.PronunciationEntries.FindAsync([id], ct);
            if (entity is null) return Results.NotFound();
            entity.Pattern = request.Pattern.Trim();
            entity.Replacement = request.Replacement.Trim();
            entity.Note = request.Note?.Trim();
            entity.IsEnabled = request.IsEnabled;
            entity.UpdatedAt = DateTime.UtcNow;
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

        group.MapGet("/novels/{novelId:long}/qa", async (
            long novelId,
            AppDbContext db,
            bool? resolved = null,
            string? severity = null,
            CancellationToken ct = default) =>
        {
            var query = db.NovelQaIssues.AsNoTracking().Where(x => x.NovelId == novelId);
            if (resolved.HasValue) query = query.Where(x => x.Resolved == resolved.Value);
            if (!string.IsNullOrWhiteSpace(severity)) query = query.Where(x => x.Severity == severity);
            var items = await query.OrderByDescending(x => x.Severity == "Error")
                .ThenBy(x => x.ScriptLineId)
                .ThenBy(x => x.Id)
                .ToListAsync(ct);
            return Results.Ok(items);
        });

        group.MapPost("/novels/{novelId:long}/qa/run", async (long novelId, AppDbContext db, CancellationToken ct) =>
        {
            var novel = await db.Novels.AsNoTracking().FirstOrDefaultAsync(x => x.Id == novelId, ct);
            if (novel is null) return Results.NotFound();

            var previous = await db.NovelQaIssues.Where(x => x.NovelId == novelId && !x.Resolved).ToListAsync(ct);
            db.NovelQaIssues.RemoveRange(previous);

            var issues = new List<NovelQaIssue>();
            var scripts = await db.ScriptLines.AsNoTracking()
                .Where(x => x.NovelId == novelId)
                .OrderBy(x => x.Order)
                .ToListAsync(ct);
            var characterVoices = await db.Characters.AsNoTracking()
                .Where(x => x.NovelId == novelId)
                .ToDictionaryAsync(x => x.Id, x => x.VoiceProfileId, ct);

            if (scripts.Any(x => string.Equals(x.Speaker, "旁白", StringComparison.OrdinalIgnoreCase)) && !novel.NarratorVoiceProfileId.HasValue)
                issues.Add(NewIssue(novelId, null, "NarratorVoiceMissing", "Error", "小说包含旁白脚本，但尚未绑定旁白音色。"));

            for (var i = 0; i < scripts.Count; i++)
            {
                var line = scripts[i];
                if (string.IsNullOrWhiteSpace(line.Text))
                    issues.Add(NewIssue(novelId, line.Id, "EmptyText", "Error", $"脚本 #{line.Order} 文本为空。"));
                if (line.SourceStart < 0 || line.SourceEnd <= line.SourceStart)
                    issues.Add(NewIssue(novelId, line.Id, "SourceOffsetMissing", "Warning", $"脚本 #{line.Order} 无法精确定位到小说原文。"));
                if (line.Text.Length > 450)
                    issues.Add(NewIssue(novelId, line.Id, "ScriptTooLong", "Warning", $"脚本 #{line.Order} 长度 {line.Text.Length} 字，建议拆分以提升 TTS 稳定性。"));
                if (line.Status == "Failed")
                    issues.Add(NewIssue(novelId, line.Id, "AudioFailed", "Error", $"脚本 #{line.Order} 音频生成失败。"));
                if (line.Status == "Completed" && (string.IsNullOrWhiteSpace(line.AudioFile) || !File.Exists(line.AudioFile)))
                    issues.Add(NewIssue(novelId, line.Id, "AudioFileMissing", "Error", $"脚本 #{line.Order} 标记为已完成，但音频文件不存在。"));
                if (line.CharacterId.HasValue && (!characterVoices.TryGetValue(line.CharacterId.Value, out var voiceId) || !voiceId.HasValue))
                    issues.Add(NewIssue(novelId, line.Id, "CharacterVoiceMissing", "Error", $"脚本 #{line.Order} 的角色“{line.Speaker}”尚未绑定音色。"));
                if (i > 0 && string.Equals(scripts[i - 1].Text.Trim(), line.Text.Trim(), StringComparison.Ordinal))
                    issues.Add(NewIssue(novelId, line.Id, "DuplicateAdjacentText", "Warning", $"脚本 #{line.Order} 与上一条文本完全重复。"));
                if (i > 0 && line.Order != scripts[i - 1].Order + 1)
                    issues.Add(NewIssue(novelId, line.Id, "OrderGap", "Warning", $"脚本顺序存在跳号：{scripts[i - 1].Order} → {line.Order}。"));
            }

            db.NovelQaIssues.AddRange(issues);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new
            {
                total = issues.Count,
                errors = issues.Count(x => x.Severity == "Error"),
                warnings = issues.Count(x => x.Severity == "Warning")
            });
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

    private static NovelQaIssue NewIssue(long novelId, long? scriptLineId, string type, string severity, string message)
        => new()
        {
            NovelId = novelId,
            ScriptLineId = scriptLineId,
            Type = type,
            Severity = severity,
            Message = message,
            Resolved = false,
            CreatedAt = DateTime.UtcNow
        };

    public sealed record UpdateTimelineRequest(string Speaker, string Text, string? Emotion);
    public sealed record PronunciationRequest(string Pattern, string Replacement, string? Note, bool IsEnabled = true);
}
