using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Api.Contracts;
using NovelSystem.Application.Contracts;
using NovelSystem.Application.Models;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Api.Endpoints;

/// <summary>小说人物、角色去重与智能音色匹配 API。</summary>
public static class CharacterEndpoints
{
    public static IEndpointRouteBuilder MapCharacterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/characters").WithTags("Characters");

        group.MapPut("/{id:long}", async (long id, UpdateCharacterRequest request, AppDbContext db) =>
        {
            var character = await db.Characters.FindAsync(id);
            if (character is null) return Results.NotFound();

            character.Name = request.Name;
            character.Gender = request.Gender;
            character.Personality = request.Personality;
            character.Description = request.Description;
            character.VoiceProfileId = request.VoiceProfileId;
            character.VoiceFile = request.VoiceFile;
            await db.SaveChangesAsync();
            return Results.Ok(character);
        });

        group.MapPost("/novel/{novelId:long}/auto-match-voices", async (
            long novelId,
            AppDbContext db,
            IAiChatClient ai,
            CancellationToken ct) =>
        {
            var novel = await db.Novels.FindAsync([novelId], ct);
            if (novel is null) return Results.NotFound();

            var characters = await db.Characters
                .Where(x => x.NovelId == novelId)
                .OrderBy(x => x.Id)
                .ToListAsync(ct);
            var voices = await db.VoiceProfiles.AsNoTracking()
                .OrderBy(x => x.Id)
                .ToListAsync(ct);

            if (voices.Count == 0)
                return Results.BadRequest(new { message = "请先创建至少一个音色档案。" });

            var characterData = characters.Select(x => new
            {
                id = x.Id,
                x.Name,
                x.Gender,
                x.Personality,
                x.Description
            });
            var voiceData = voices.Select(x => new
            {
                id = x.Id,
                x.Name,
                x.Language,
                x.VoiceDescription,
                x.VoiceTags,
                referenceText = x.ReferenceText.Length > 160 ? x.ReferenceText[..160] : x.ReferenceText
            });

            var raw = await ai.ChatJsonStrictTrackedAsync(
                "你是专业中文有声书配音导演。根据人物类型和音色描述做最合适的声线匹配，只输出JSON。",
                """
                为小说人物选择最匹配的音色。允许多个次要人物复用同一音色，但性别、年龄感、性格气质应优先匹配。
                旁白选择适合长时间叙事、稳定自然、不抢角色戏份的音色。
                仅从给定 voiceProfileId 中选择。
                输出格式：
                {"matches":[{"characterId":1,"voiceProfileId":2,"reason":"简短原因"}],"narratorVoiceProfileId":2}

                人物：
                """ + JsonSerializer.Serialize(characterData) +
                "\n\n可用音色：\n" + JsonSerializer.Serialize(voiceData),
                new AiCallContext(novelId, null, "AutoMatchCharacterVoices"),
                ct);

            using var json = JsonDocument.Parse(raw);
            var root = json.RootElement;
            var reasons = new List<object>();

            if (root.TryGetProperty("matches", out var matches) && matches.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in matches.EnumerateArray())
                {
                    if (!item.TryGetProperty("characterId", out var charIdProp) ||
                        !item.TryGetProperty("voiceProfileId", out var voiceIdProp))
                        continue;

                    var characterId = charIdProp.GetInt64();
                    var voiceId = voiceIdProp.GetInt64();
                    var character = characters.FirstOrDefault(x => x.Id == characterId);
                    if (character is null || voices.All(x => x.Id != voiceId))
                        continue;

                    character.VoiceProfileId = voiceId;
                    reasons.Add(new
                    {
                        character.Id,
                        character.Name,
                        voiceProfileId = voiceId,
                        reason = item.TryGetProperty("reason", out var reason) ? reason.GetString() : null
                    });
                }
            }

            if (root.TryGetProperty("narratorVoiceProfileId", out var narratorProp) &&
                narratorProp.ValueKind == JsonValueKind.Number)
            {
                var narratorId = narratorProp.GetInt64();
                if (voices.Any(x => x.Id == narratorId))
                    novel.NarratorVoiceProfileId = narratorId;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new
            {
                matched = reasons.Count,
                narratorVoiceProfileId = novel.NarratorVoiceProfileId,
                items = reasons
            });
        });

        group.MapPost("/novel/{novelId:long}/deduplicate", async (
            long novelId,
            AppDbContext db,
            IAiChatClient ai,
            CancellationToken ct) =>
        {
            var characters = await db.Characters
                .Where(x => x.NovelId == novelId)
                .OrderBy(x => x.Id)
                .ToListAsync(ct);

            if (characters.Count < 2)
                return Results.Ok(new { mergedGroups = 0, removedCharacters = 0 });

            // 补充每个角色少量真实台词，帮助模型识别“本名/昵称/称谓”是否同一人。
            var samples = await db.ScriptLines.AsNoTracking()
                .Where(x => x.NovelId == novelId && x.CharacterId != null)
                .OrderBy(x => x.Order)
                .Select(x => new { x.CharacterId, x.Speaker, x.Text })
                .ToListAsync(ct);

            var data = characters.Select(x => new
            {
                id = x.Id,
                x.Name,
                x.Gender,
                x.Personality,
                x.Description,
                samples = samples.Where(s => s.CharacterId == x.Id)
                    .Take(3)
                    .Select(s => s.Text.Length > 100 ? s.Text[..100] : s.Text)
                    .ToArray()
            });

            var raw = await ai.ChatJsonStrictTrackedAsync(
                "你是小说人物实体消歧专家。识别同一人物的本名、昵称、尊称、亲昵称呼，只输出JSON。",
                """
                判断下列角色是否存在“实际为同一个人物但名称不同”的情况。
                只有高度确定时才合并；同姓、同身份、相似性格但不是同一个人绝不能合并。
                primaryId 应选择信息最完整、最正式名称对应的角色。
                primaryName 可纠正为最规范的人物名称。
                输出：{"groups":[{"primaryId":1,"primaryName":"规范名称","aliasIds":[2,3],"reason":"原因"}]}
                没有重复则输出 {"groups":[]}。

                角色数据：
                """ + JsonSerializer.Serialize(data),
                new AiCallContext(novelId, null, "DeduplicateCharacters"),
                ct);

            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
                return Results.Ok(new { mergedGroups = 0, removedCharacters = 0 });

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var mergedGroups = 0;
            var removed = 0;
            var details = new List<object>();

            foreach (var groupItem in groups.EnumerateArray())
            {
                if (!groupItem.TryGetProperty("primaryId", out var primaryIdProp) ||
                    !groupItem.TryGetProperty("aliasIds", out var aliasIdsProp) ||
                    aliasIdsProp.ValueKind != JsonValueKind.Array)
                    continue;

                var primaryId = primaryIdProp.GetInt64();
                var primary = characters.FirstOrDefault(x => x.Id == primaryId);
                if (primary is null) continue;

                var aliasIds = aliasIdsProp.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.Number)
                    .Select(x => x.GetInt64())
                    .Where(x => x != primaryId)
                    .Distinct()
                    .ToList();

                var aliases = characters.Where(x => aliasIds.Contains(x.Id)).ToList();
                if (aliases.Count == 0) continue;

                var primaryName = groupItem.TryGetProperty("primaryName", out var nameProp)
                    ? nameProp.GetString()?.Trim()
                    : null;
                if (!string.IsNullOrWhiteSpace(primaryName))
                    primary.Name = primaryName;

                // 主角色没有音色时，继承别名角色中已经绑定的音色。
                primary.VoiceProfileId ??= aliases.Select(x => x.VoiceProfileId).FirstOrDefault(x => x.HasValue);
                primary.Gender ??= aliases.Select(x => x.Gender).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                primary.Personality ??= aliases.Select(x => x.Personality).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                primary.Description ??= aliases.Select(x => x.Description).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                var aliasNames = aliases.Select(x => x.Name).ToList();
                await db.ScriptLines
                    .Where(x => x.NovelId == novelId && aliasIds.Contains(x.CharacterId ?? 0))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.CharacterId, primary.Id)
                        .SetProperty(x => x.Speaker, primary.Name), ct);

                // 修复解析时未正确关联 CharacterId、但 Speaker 使用别名称呼的行。
                await db.ScriptLines
                    .Where(x => x.NovelId == novelId && aliasNames.Contains(x.Speaker))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.CharacterId, primary.Id)
                        .SetProperty(x => x.Speaker, primary.Name), ct);

                db.Characters.RemoveRange(aliases);
                removed += aliases.Count;
                mergedGroups++;
                details.Add(new
                {
                    primary.Id,
                    primary.Name,
                    aliases = aliasNames,
                    reason = groupItem.TryGetProperty("reason", out var reason) ? reason.GetString() : null
                });
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Results.Ok(new { mergedGroups, removedCharacters = removed, details });
        });

        return app;
    }
}