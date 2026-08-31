using Microsoft.EntityFrameworkCore;
using NovelSystem.Api.Contracts;
using NovelSystem.Application.Contracts;
using NovelSystem.Application.Models;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Api.Endpoints;

/// <summary>Qwen3-TTS 音色档案管理 API。</summary>
public static class VoiceProfileEndpoints
{
    public static IEndpointRouteBuilder MapVoiceProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/voice-profiles").WithTags("VoiceProfiles");

        group.MapGet("/", async (
            AppDbContext db,
            int page = 1,
            int pageSize = 12,
            string? keyword = null,
            string? language = null,
            string? status = null) =>
        {
            NormalizePaging(ref page, ref pageSize);
            var query = db.VoiceProfiles.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var value = keyword.Trim();
                query = query.Where(x =>
                    x.Name.Contains(value) ||
                    x.ReferenceAudioFile.Contains(value) ||
                    x.ReferenceText.Contains(value) ||
                    (x.VoiceDescription != null && x.VoiceDescription.Contains(value)) ||
                    (x.VoiceTags != null && x.VoiceTags.Contains(value)));
            }

            if (!string.IsNullOrWhiteSpace(language))
                query = query.Where(x => x.Language == language);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(x => x.Status == status);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Results.Ok(new { items, total, page, pageSize });
        });

        group.MapGet("/options", async (AppDbContext db) =>
            await db.VoiceProfiles.AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.Language,
                    x.Status,
                    x.VoiceDescription,
                    x.VoiceTags
                })
                .ToListAsync());

        group.MapPost("/", async (SaveVoiceProfileRequest request, AppDbContext db) =>
        {
            ValidateRequest(request.ReferenceText, request.UseXVector);

            var entity = new VoiceProfile
            {
                Name = request.Name.Trim(),
                ReferenceAudioFile = request.ReferenceAudioFile,
                ReferenceText = request.ReferenceText,
                UseXVector = request.UseXVector,
                Language = request.Language,
                VoiceDescription = request.VoiceDescription,
                VoiceTags = request.VoiceTags,
                Status = "Ready"
            };

            db.VoiceProfiles.Add(entity);
            await db.SaveChangesAsync();
            return Results.Ok(entity);
        });

        group.MapPost("/batch", async (
            BatchVoiceProfileRequest request,
            AppDbContext db,
            ITtsClient tts,
            CancellationToken cancellationToken) =>
        {
            ValidateRequest(request.ReferenceText, request.UseXVector);

            var directory = await db.Settings
                .Where(x => x.Key == "VoiceDirectory")
                .Select(x => x.Value)
                .FirstOrDefaultAsync(cancellationToken) ?? "voices";

            if (!Directory.Exists(directory))
                return Results.BadRequest(new { message = $"音色目录不存在：{directory}" });

            var files = Directory.EnumerateFiles(directory, "*.wav", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var created = new List<VoiceProfile>();
            var skipped = new List<string>();

            foreach (var file in files)
            {
                var fullPath = Path.GetFullPath(file);
                var name = Path.GetFileNameWithoutExtension(file);

                var exists = await db.VoiceProfiles.AnyAsync(
                    x => x.ReferenceAudioFile == fullPath || x.Name == name,
                    cancellationToken);

                if (exists && request.SkipExisting)
                {
                    skipped.Add(name);
                    continue;
                }

                var entity = new VoiceProfile
                {
                    Name = name,
                    ReferenceAudioFile = fullPath,
                    ReferenceText = request.ReferenceText,
                    UseXVector = request.UseXVector,
                    Language = request.Language,
                    VoiceDescription = request.VoiceDescription,
                    VoiceTags = request.VoiceTags,
                    Status = "Ready"
                };

                db.VoiceProfiles.Add(entity);
                created.Add(entity);
            }

            await db.SaveChangesAsync(cancellationToken);

            var promptErrors = new List<object>();
            if (request.BuildPrompt)
            {
                foreach (var entity in created)
                {
                    try
                    {
                        entity.Status = "BuildingPrompt";
                        await db.SaveChangesAsync(cancellationToken);
                        await tts.CreatePromptAsync(entity, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        entity.Status = "Failed";
                        entity.UpdatedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(cancellationToken);
                        promptErrors.Add(new { entity.Id, entity.Name, error = ex.Message });
                    }
                }
            }

            return Results.Ok(new
            {
                scanned = files.Count,
                created = created.Count,
                skipped = skipped.Count,
                createdItems = created,
                skippedItems = skipped,
                promptErrors
            });
        });

        group.MapPut("/{id:long}", async (long id, SaveVoiceProfileRequest request, AppDbContext db) =>
        {
            ValidateRequest(request.ReferenceText, request.UseXVector);

            var entity = await db.VoiceProfiles.FindAsync(id);
            if (entity is null) return Results.NotFound();

            var audioChanged =
                !string.Equals(entity.ReferenceAudioFile, request.ReferenceAudioFile, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(entity.ReferenceText, request.ReferenceText, StringComparison.Ordinal)
                || entity.UseXVector != request.UseXVector;

            entity.Name = request.Name.Trim();
            entity.ReferenceAudioFile = request.ReferenceAudioFile;
            entity.ReferenceText = request.ReferenceText;
            entity.UseXVector = request.UseXVector;
            entity.Language = request.Language;
            entity.VoiceDescription = request.VoiceDescription;
            entity.VoiceTags = request.VoiceTags;
            entity.UpdatedAt = DateTime.UtcNow;

            if (audioChanged)
            {
                entity.PromptFile = null;
                entity.Status = "Ready";
            }

            await db.SaveChangesAsync();
            return Results.Ok(entity);
        });

        group.MapPost("/{id:long}/prompt", async (long id, AppDbContext db, ITtsClient tts) =>
        {
            var entity = await db.VoiceProfiles.FindAsync(id);
            if (entity is null) return Results.NotFound();

            entity.Status = "BuildingPrompt";
            await db.SaveChangesAsync();

            try
            {
                var prompt = await tts.CreatePromptAsync(entity);
                return Results.Ok(new { promptFile = prompt, entity.Status });
            }
            catch (Exception ex)
            {
                entity.Status = "Failed";
                entity.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                return Results.Problem(ex.Message);
            }
        });

        group.MapPost("/{id:long}/describe", async (
            long id,
            AppDbContext db,
            IAiChatClient ai,
            CancellationToken cancellationToken) =>
        {
            var entity = await db.VoiceProfiles.FindAsync(id);
            if (entity is null) return Results.NotFound();

            var raw = await ai.ChatJsonTrackedAsync(
                "你是中文配音导演。根据音色档案信息输出JSON，不解释。",
                """
                根据以下音色名称、语言和参考文本，推断适合的人物声音类型。
                输出：
                {"description":"一句完整的声音描述","tags":["标签1","标签2","标签3"]}
                标签优先覆盖：性别、年龄感、音高、音色质感、语速、情绪气质、适合角色类型。
                音色名称：
                """ + entity.Name + "
语言：" + entity.Language + "
参考文本：" + entity.ReferenceText,
                new AiCallContext(null, null, "DescribeVoiceProfile"),
                cancellationToken);

            using var json = System.Text.Json.JsonDocument.Parse(raw);
            var root = json.RootElement;
            entity.VoiceDescription = root.TryGetProperty("description", out var desc) ? desc.GetString() : entity.VoiceDescription;
            if (root.TryGetProperty("tags", out var tags) && tags.ValueKind == System.Text.Json.JsonValueKind.Array)
                entity.VoiceTags = string.Join(",", tags.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)));
            entity.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(entity);
        });

        group.MapDelete("/{id:long}", async (long id, AppDbContext db) =>
        {
            var entity = await db.VoiceProfiles.FindAsync(id);
            if (entity is null) return Results.NotFound();

            var inUse = await db.Characters.AnyAsync(x => x.VoiceProfileId == id)
                        || await db.Novels.AnyAsync(x => x.NarratorVoiceProfileId == id);
            if (inUse)
                return Results.Conflict(new { message = "该音色仍被小说人物使用，不能删除。" });

            db.VoiceProfiles.Remove(entity);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        return app;
    }

    private static void ValidateRequest(string referenceText, bool useXVector)
    {
        if (!useXVector && string.IsNullOrWhiteSpace(referenceText))
            throw new BadHttpRequestException("未启用 x-vector 时必须配置参考音频文本。");
    }

    private static void NormalizePaging(ref int page, ref int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 5, 200);
    }
}