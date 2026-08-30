using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Application.Contracts;
using NovelSystem.Application.Models;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Persistence;
using NovelSystem.Infrastructure.Jobs;

namespace NovelSystem.Infrastructure.Services;

/// <summary>负责长小说分块解析、人物去重、脚本顺序化和断点续跑。</summary>
public sealed class NovelAnalysisService(AppDbContext db, IAiChatClient aiClient) : INovelAnalysisService
{
    public async Task AnalyzeAsync(long novelId, long jobId, CancellationToken cancellationToken = default)
    {
        var novel = await db.Novels.FindAsync([novelId], cancellationToken)
                    ?? throw new InvalidOperationException("小说不存在。");
        var job = await db.Jobs.FindAsync([jobId], cancellationToken)
                  ?? throw new InvalidOperationException("任务不存在。");

        var chunkText = db.Settings.FirstOrDefault(x => x.Key == "AiChunkSize")?.Value ?? "12000";
        var chunkSize = int.TryParse(chunkText, out var configured)
            ? Math.Clamp(configured, 1000, 100000)
            : 12000;

        novel.Status = NovelStatus.Analyzing;
        await db.SaveChangesAsync(cancellationToken);

        var chunks = Split(novel.Content, chunkSize).ToList();
        job.TotalSteps = chunks.Count;

        // Checkpoint 表示已经完成的块数，失败重试时从该位置继续。
        var startIndex = Math.Clamp(job.Checkpoint, 0, chunks.Count);
        var scriptOrder = await db.ScriptLines
            .Where(x => x.NovelId == novelId)
            .Select(x => x.Order)
            .DefaultIfEmpty(0)
            .MaxAsync(cancellationToken);

        for (var index = startIndex; index < chunks.Count; index++)
        {
            await db.Entry(job).ReloadAsync(cancellationToken);
            if (job.Status == "Stopping")
                throw new OperationCanceledException("任务已由用户停止。");

            var raw = await aiClient.ChatAsync(
                "你是专业小说结构分析器。只输出合法 JSON。",
                "提取人物和多角色 TTS 脚本。JSON格式：{\"characters\":[{\"name\":\"\",\"gender\":\"\",\"personality\":\"\",\"description\":\"\"}],\"scripts\":[{\"speaker\":\"人物名或旁白\",\"text\":\"\",\"emotion\":\"\"}]}\n\n" + chunks[index],
                cancellationToken);

            var result = JsonSerializer.Deserialize<AiAnalysisResult>(
                NormalizeJson(raw),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AiAnalysisResult();

            foreach (var item in result.Characters.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
            {
                var exists = await db.Characters.AnyAsync(
                    x => x.NovelId == novelId && x.Name == item.Name,
                    cancellationToken);

                if (!exists)
                {
                    db.Characters.Add(new Character
                    {
                        NovelId = novelId,
                        Name = item.Name.Trim(),
                        Gender = item.Gender,
                        Personality = item.Personality,
                        Description = item.Description
                    });
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            foreach (var item in result.Scripts.Where(x => !string.IsNullOrWhiteSpace(x.Text)))
            {
                var character = await db.Characters.FirstOrDefaultAsync(
                    x => x.NovelId == novelId && x.Name == item.Speaker,
                    cancellationToken);

                db.ScriptLines.Add(new ScriptLine
                {
                    NovelId = novelId,
                    CharacterId = character?.Id,
                    Order = ++scriptOrder,
                    Speaker = string.IsNullOrWhiteSpace(item.Speaker) ? "旁白" : item.Speaker,
                    Text = item.Text.Trim(),
                    Emotion = item.Emotion
                });
            }

            job.Checkpoint = index + 1;
            job.Progress = (int)Math.Round(job.Checkpoint * 100d / Math.Max(chunks.Count, 1));
            JobTimingCalculator.Refresh(job);
            await db.SaveChangesAsync(cancellationToken);
        }

        novel.Status = NovelStatus.Analyzed;
        job.Progress = 100;
        job.Checkpoint = chunks.Count;
        JobTimingCalculator.Refresh(job);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<string> Split(string text, int chunkSize)
    {
        for (var i = 0; i < text.Length; i += chunkSize)
            yield return text.Substring(i, Math.Min(chunkSize, text.Length - i));
    }

    private static string NormalizeJson(string value)
    {
        var text = value.Trim();
        var first = text.IndexOf('{');
        var last = text.LastIndexOf('}');
        return first >= 0 && last >= first ? text[first..(last + 1)] : text;
    }
}