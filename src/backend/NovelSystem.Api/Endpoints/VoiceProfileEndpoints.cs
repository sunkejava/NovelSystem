using Microsoft.EntityFrameworkCore;
using NovelSystem.Api.Contracts;
using NovelSystem.Application.Contracts;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Api.Endpoints;

/// <summary>
/// Qwen3-TTS 音色档案管理 API。
/// </summary>
public static class VoiceProfileEndpoints
{
    public static IEndpointRouteBuilder MapVoiceProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/voice-profiles").WithTags("VoiceProfiles");

        group.MapGet("/", async (AppDbContext db) =>
            await db.VoiceProfiles.OrderByDescending(x => x.Id).ToListAsync());

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

        group.MapDelete("/{id:long}", async (long id, AppDbContext db) =>
        {
            var entity = await db.VoiceProfiles.FindAsync(id);
            if (entity is null) return Results.NotFound();

            var inUse = await db.Characters.AnyAsync(x => x.VoiceProfileId == id);
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
}