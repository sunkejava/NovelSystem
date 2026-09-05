using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Api.Contracts;
using NovelSystem.Application.Contracts;
using NovelSystem.Application.Models;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Api.Endpoints;

/// <summary>小说人物、角色去重与智能音色匹配 API。</summary>
public static class CharacterEndpoints
{
    private const int DedupBatchSize = 36;

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
                return Results.Ok(new { mergedGroups = 0, removedCharacters = 0, batches = 0, crossBatches = 0 });

            // 第一阶段：只让模型看到小批量角色。大小说即使有数百/上千角色，也不会一次性塞入上下文。
            var proposals = new List<DedupProposal>();
            var batchCount = (int)Math.Ceiling(characters.Count / (double)DedupBatchSize);
            for (var offset = 0; offset < characters.Count; offset += DedupBatchSize)
            {
                var batch = characters.Skip(offset).Take(DedupBatchSize).ToList();
                var batchProposals = await AnalyzeDedupBatchAsync(
                    db,
                    ai,
                    novelId,
                    batch,
                    "DeduplicateCharactersBatch",
                    offset / DedupBatchSize + 1,
                    batchCount,
                    ct);
                proposals.AddRange(batchProposals);
            }

            // 第二阶段：跨批次再次交叉归并。
            // 先按“性别 + 性格/描述 + 名称长度”重新排序，使第一轮不同批次但特征接近的人重新进入同一小批次。
            // 仍然严格控制单次送给 AI 的角色数量。
            var aliasIdsFromLocal = proposals.SelectMany(x => x.AliasIds).ToHashSet();
            var representatives = characters
                .Where(x => !aliasIdsFromLocal.Contains(x.Id))
                .OrderBy(BuildSemanticSortKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id)
                .ToList();

            var crossBatchCount = (int)Math.Ceiling(representatives.Count / (double)DedupBatchSize);
            for (var offset = 0; offset < representatives.Count; offset += DedupBatchSize)
            {
                var batch = representatives.Skip(offset).Take(DedupBatchSize).ToList();
                if (batch.Count < 2) continue;

                var batchProposals = await AnalyzeDedupBatchAsync(
                    db,
                    ai,
                    novelId,
                    batch,
                    "DeduplicateCharactersCrossBatch",
                    offset / DedupBatchSize + 1,
                    crossBatchCount,
                    ct);
                proposals.AddRange(batchProposals);
            }

            if (proposals.Count == 0)
                return Results.Ok(new
                {
                    mergedGroups = 0,
                    removedCharacters = 0,
                    batches = batchCount,
                    crossBatches = crossBatchCount
                });

            // 所有 AI 调用结束后才统一计算连通分量并落库，避免第一批删掉角色后影响后续跨批校验。
            var components = BuildDedupComponents(characters, proposals);
            if (components.Count == 0)
                return Results.Ok(new
                {
                    mergedGroups = 0,
                    removedCharacters = 0,
                    batches = batchCount,
                    crossBatches = crossBatchCount
                });

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var removed = 0;
            var details = new List<object>();

            foreach (var component in components)
            {
                var primary = component.Primary;
                var aliases = component.Aliases;
                if (aliases.Count == 0) continue;

                if (!string.IsNullOrWhiteSpace(component.PrimaryName))
                    primary.Name = component.PrimaryName!;

                primary.VoiceProfileId ??= aliases.Select(x => x.VoiceProfileId).FirstOrDefault(x => x.HasValue);
                primary.Gender ??= aliases.Select(x => x.Gender).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                primary.Personality ??= aliases.Select(x => x.Personality).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                primary.Description ??= aliases.Select(x => x.Description).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                var aliasIds = aliases.Select(x => x.Id).ToList();
                var aliasNames = aliases.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                await db.ScriptLines
                    .Where(x => x.NovelId == novelId && aliasIds.Contains(x.CharacterId ?? 0))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.CharacterId, primary.Id)
                        .SetProperty(x => x.Speaker, primary.Name), ct);

                await db.ScriptLines
                    .Where(x => x.NovelId == novelId && aliasNames.Contains(x.Speaker))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.CharacterId, primary.Id)
                        .SetProperty(x => x.Speaker, primary.Name), ct);

                db.Characters.RemoveRange(aliases);
                removed += aliases.Count;
                details.Add(new
                {
                    primary.Id,
                    primary.Name,
                    aliases = aliasNames,
                    component.Reason
                });
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Results.Ok(new
            {
                mergedGroups = details.Count,
                removedCharacters = removed,
                batches = batchCount,
                crossBatches = crossBatchCount,
                details
            });
        });

        return app;
    }

    private static async Task<List<DedupProposal>> AnalyzeDedupBatchAsync(
        AppDbContext db,
        IAiChatClient ai,
        long novelId,
        IReadOnlyList<Character> batch,
        string operation,
        int batchIndex,
        int batchTotal,
        CancellationToken ct)
    {
        if (batch.Count < 2) return [];

        var data = new List<object>(batch.Count);
        foreach (var character in batch)
        {
            // 每个角色最多取 3 条真实台词；宁可多做几个轻量 SQLite 查询，也不把整本脚本一次性加载到内存。
            var samples = await db.ScriptLines.AsNoTracking()
                .Where(x => x.NovelId == novelId && x.CharacterId == character.Id)
                .OrderBy(x => x.Order)
                .Select(x => x.Text)
                .Take(3)
                .ToListAsync(ct);

            data.Add(new
            {
                id = character.Id,
                character.Name,
                character.Gender,
                character.Personality,
                description = TrimForAi(character.Description, 280),
                samples = samples.Select(x => TrimForAi(x, 120)).ToArray()
            });
        }

        var raw = await ai.ChatJsonStrictTrackedAsync(
            "你是中文小说人物实体消歧专家。识别同一人物的本名、昵称、尊称、亲昵称呼，只输出JSON。",
            """
            判断本批角色是否存在“实际为同一个人物但名称不同”的情况。
            只有高度确定时才合并；同姓、同身份、相似性格但不是同一个人绝不能合并。
            primaryId 应选择信息最完整、最正式名称对应的角色。
            primaryName 可纠正为最规范的人物名称。
            只允许使用本批数据中真实存在的 ID。
            输出：{"groups":[{"primaryId":1,"primaryName":"规范名称","aliasIds":[2,3],"reason":"原因"}]}
            没有重复则输出 {"groups":[]}。

            角色数据：
            """ + JsonSerializer.Serialize(data),
            new AiCallContext(novelId, null, operation, batchIndex, batchTotal),
            ct);

        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
            return [];

        var validIds = batch.Select(x => x.Id).ToHashSet();
        var result = new List<DedupProposal>();
        foreach (var item in groups.EnumerateArray())
        {
            if (!item.TryGetProperty("primaryId", out var primaryProp) ||
                primaryProp.ValueKind != JsonValueKind.Number ||
                !item.TryGetProperty("aliasIds", out var aliasProp) ||
                aliasProp.ValueKind != JsonValueKind.Array)
                continue;

            var primaryId = primaryProp.GetInt64();
            if (!validIds.Contains(primaryId)) continue;

            var aliasIds = aliasProp.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Number)
                .Select(x => x.GetInt64())
                .Where(x => x != primaryId && validIds.Contains(x))
                .Distinct()
                .ToList();
            if (aliasIds.Count == 0) continue;

            result.Add(new DedupProposal(
                primaryId,
                item.TryGetProperty("primaryName", out var name) ? name.GetString()?.Trim() : null,
                aliasIds,
                item.TryGetProperty("reason", out var reason) ? reason.GetString() : null));
        }

        return result;
    }

    private static List<DedupComponent> BuildDedupComponents(
        IReadOnlyList<Character> characters,
        IReadOnlyList<DedupProposal> proposals)
    {
        var byId = characters.ToDictionary(x => x.Id);
        var parent = characters.ToDictionary(x => x.Id, x => x.Id);

        long Find(long id)
        {
            while (parent[id] != id)
            {
                parent[id] = parent[parent[id]];
                id = parent[id];
            }
            return id;
        }

        void Union(long a, long b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb) parent[rb] = ra;
        }

        foreach (var proposal in proposals)
            foreach (var aliasId in proposal.AliasIds)
                if (byId.ContainsKey(proposal.PrimaryId) && byId.ContainsKey(aliasId))
                    Union(proposal.PrimaryId, aliasId);

        var groups = characters
            .GroupBy(x => Find(x.Id))
            .Where(x => x.Count() > 1)
            .ToList();

        var result = new List<DedupComponent>();
        foreach (var group in groups)
        {
            var ids = group.Select(x => x.Id).ToHashSet();
            var related = proposals.Where(x => ids.Contains(x.PrimaryId) || x.AliasIds.Any(ids.Contains)).ToList();
            var primaryVotes = related
                .GroupBy(x => x.PrimaryId)
                .ToDictionary(x => x.Key, x => x.Count());

            var primary = group
                .OrderByDescending(x => primaryVotes.GetValueOrDefault(x.Id))
                .ThenByDescending(InformationScore)
                .ThenBy(x => x.Id)
                .First();

            var preferredName = related
                .Where(x => x.PrimaryId == primary.Id && !string.IsNullOrWhiteSpace(x.PrimaryName))
                .Select(x => x.PrimaryName)
                .FirstOrDefault();

            var reason = string.Join("；", related
                .Select(x => x.Reason)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .Take(4));

            result.Add(new DedupComponent(
                primary,
                group.Where(x => x.Id != primary.Id).ToList(),
                preferredName,
                reason));
        }

        return result;
    }

    private static int InformationScore(Character x)
        => (x.Name?.Length ?? 0) * 3 +
           (x.Description?.Length ?? 0) +
           (x.Personality?.Length ?? 0) * 2 +
           (string.IsNullOrWhiteSpace(x.Gender) ? 0 : 8) +
           (x.VoiceProfileId.HasValue ? 20 : 0);

    private static string BuildSemanticSortKey(Character x)
        => string.Join('|',
            x.Gender?.Trim() ?? string.Empty,
            TrimForAi(x.Personality, 24),
            TrimForAi(x.Description, 36),
            (x.Name?.Length ?? 0).ToString("D3"),
            x.Name?.Trim() ?? string.Empty);

    private static string TrimForAi(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Trim();
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private sealed record DedupProposal(
        long PrimaryId,
        string? PrimaryName,
        List<long> AliasIds,
        string? Reason);

    private sealed record DedupComponent(
        Character Primary,
        List<Character> Aliases,
        string? PrimaryName,
        string? Reason);
}
