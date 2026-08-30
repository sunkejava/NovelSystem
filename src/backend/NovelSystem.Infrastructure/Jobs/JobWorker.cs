using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NovelSystem.Application.Contracts;
using NovelSystem.Application.Models;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Infrastructure.Jobs;

/// <summary>
/// 后台任务执行器。统一承载小说解析、写作风格学习、TTS 生成及音频合并，
/// 并支持停止、继续和失败断点重试。
/// </summary>
public sealed class JobWorker(IServiceScopeFactory scopeFactory, JobQueue queue) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverPendingJobsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var message = await queue.DequeueAsync(stoppingToken);
            await ExecuteJobAsync(message, stoppingToken);
        }
    }

    private async Task RecoverPendingJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = await db.Jobs
            .Where(x => x.Status == "Queued" || x.Status == "Running" || x.Status == "Stopping")
            .ToListAsync(cancellationToken);

        foreach (var job in pending.Where(x => !string.IsNullOrWhiteSpace(x.Payload)))
        {
            if (job.Status == "Stopping")
            {
                job.Status = "Stopped";
                JobTimingCalculator.MarkTerminal(job);
                continue;
            }

            job.Status = "Queued";
            queue.Enqueue(new JobMessage(job.Id, job.Type, job.Payload!));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ExecuteJobAsync(JobMessage message, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.Jobs.FindAsync([message.JobId], cancellationToken);

        if (job is null || job.Status == "Stopped")
            return;

        if (job.Status == "Stopping")
        {
            job.Status = "Stopped";
            JobTimingCalculator.MarkTerminal(job);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            job.Status = "Running";
            job.StartedAt ??= DateTime.UtcNow;
            job.FinishedAt = null;
            job.Error = null;
            JobTimingCalculator.Refresh(job);
            await db.SaveChangesAsync(cancellationToken);

            using var payload = JsonDocument.Parse(message.Payload);
            var novelId = payload.RootElement.TryGetProperty("novelId", out var novelIdProperty)
                ? novelIdProperty.GetInt64()
                : 0;

            switch (message.Type)
            {
                case "AnalyzeNovel":
                    await scope.ServiceProvider.GetRequiredService<INovelAnalysisService>()
                        .AnalyzeAsync(novelId, job.Id, cancellationToken);
                    break;

                case "GenerateAudio":
                    await GenerateAudioAsync(scope.ServiceProvider, db, job, novelId, cancellationToken);
                    break;

                case "GenerateAudioSegment":
                    await GenerateAudioSegmentAsync(
                        scope.ServiceProvider,
                        db,
                        job,
                        payload.RootElement.GetProperty("scriptLineId").GetInt64(),
                        cancellationToken);
                    break;

                case "MergeAudio":
                    await MergeAudioAsync(scope.ServiceProvider, db, job, novelId, cancellationToken);
                    break;

                case "LearnWritingStyle":
                    await LearnWritingStyleAsync(
                        scope.ServiceProvider,
                        db,
                        job,
                        novelId,
                        cancellationToken);
                    break;

                default:
                    throw new InvalidOperationException($"未知任务类型：{message.Type}");
            }

            job.Status = "Completed";
            job.Progress = 100;
            JobTimingCalculator.MarkTerminal(job);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException ex) when (ex.Message.Contains("用户停止"))
        {
            var finishedAt = DateTime.UtcNow;
            var startedAt = await db.Jobs.AsNoTracking()
                .Where(x => x.Id == job.Id)
                .Select(x => x.StartedAt)
                .SingleAsync(CancellationToken.None);

            var elapsed = startedAt.HasValue
                ? Math.Max(0L, (long)(finishedAt - JobTimingCalculator.EnsureUtc(startedAt.Value)).TotalMilliseconds)
                : 0L;

            // 只更新终态字段，不触碰 Checkpoint / Progress / TotalSteps，
            // 避免被当前 DbContext 中的旧跟踪值覆盖真实断点。
            await db.Jobs
                .Where(x => x.Id == job.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, "Stopped")
                    .SetProperty(x => x.FinishedAt, finishedAt)
                    .SetProperty(x => x.ElapsedMilliseconds, elapsed)
                    .SetProperty(x => x.EstimatedCompletionAt, (DateTime?)null),
                    CancellationToken.None);
        }
        catch (Exception ex)
        {
            var finishedAt = DateTime.UtcNow;
            var startedAt = await db.Jobs.AsNoTracking()
                .Where(x => x.Id == job.Id)
                .Select(x => x.StartedAt)
                .SingleAsync(CancellationToken.None);

            var elapsed = startedAt.HasValue
                ? Math.Max(0L, (long)(finishedAt - JobTimingCalculator.EnsureUtc(startedAt.Value)).TotalMilliseconds)
                : 0L;

            // 失败时仅更新失败信息和结束时间，明确保留最后成功块的断点。
            await db.Jobs
                .Where(x => x.Id == job.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, "Failed")
                    .SetProperty(x => x.Error, ex.ToString())
                    .SetProperty(x => x.FinishedAt, finishedAt)
                    .SetProperty(x => x.ElapsedMilliseconds, elapsed)
                    .SetProperty(x => x.EstimatedCompletionAt, (DateTime?)null),
                    CancellationToken.None);
        }
    }

    private static async Task GenerateAudioAsync(
        IServiceProvider services,
        AppDbContext db,
        JobRecord job,
        long novelId,
        CancellationToken cancellationToken)
    {
        var tts = services.GetRequiredService<ITtsClient>();
        var lines = await db.ScriptLines
            .Where(x => x.NovelId == novelId)
            .OrderBy(x => x.Order)
            .ToListAsync(cancellationToken);

        if (lines.Count == 0)
            throw new InvalidOperationException("请先完成小说 AI 解析。");

        job.TotalSteps = lines.Count + 1;
        await db.SaveChangesAsync(cancellationToken);

        var files = new List<string>();

        for (var index = 0; index < lines.Count; index++)
        {
            await EnsureNotStoppingAsync(db, job, cancellationToken);
            var line = lines[index];

            if (line.Status == "Completed" &&
                !string.IsNullOrWhiteSpace(line.AudioFile) &&
                File.Exists(line.AudioFile))
            {
                files.Add(line.AudioFile);
                job.Checkpoint = Math.Max(job.Checkpoint, index + 1);
                job.Progress = (int)Math.Round((index + 1) * 90d / lines.Count);
                JobTimingCalculator.Refresh(job);
                continue;
            }

            await GenerateLineAsync(tts, db, line, cancellationToken);
            files.Add(line.AudioFile!);

            job.Checkpoint = index + 1;
            job.Progress = (int)Math.Round((index + 1) * 90d / lines.Count);
            JobTimingCalculator.Refresh(job);
            await db.SaveChangesAsync(cancellationToken);
        }

        job.Result = await tts.MergeToMp3Async(
            files,
            $"storage/output/novel-{novelId}.mp3",
            cancellationToken);

        job.Checkpoint = lines.Count + 1;
        job.Progress = 100;
        JobTimingCalculator.Refresh(job);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task GenerateAudioSegmentAsync(
        IServiceProvider services,
        AppDbContext db,
        JobRecord job,
        long scriptLineId,
        CancellationToken cancellationToken)
    {
        var tts = services.GetRequiredService<ITtsClient>();
        var line = await db.ScriptLines.FindAsync([scriptLineId], cancellationToken)
                   ?? throw new InvalidOperationException("脚本片段不存在。");

        job.TotalSteps = 1;
        await db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(line.AudioFile) && File.Exists(line.AudioFile))
            File.Delete(line.AudioFile);

        line.AudioFile = null;
        line.Status = "Pending";
        await db.SaveChangesAsync(cancellationToken);

        await GenerateLineAsync(tts, db, line, cancellationToken);

        job.Checkpoint = 1;
        job.Progress = 100;
        job.Result = line.AudioFile;
        JobTimingCalculator.Refresh(job);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task GenerateLineAsync(
        ITtsClient tts,
        AppDbContext db,
        ScriptLine line,
        CancellationToken cancellationToken)
    {
        var character = line.CharacterId is null
            ? null
            : await db.Characters.FindAsync([line.CharacterId.Value], cancellationToken);

        if (character?.VoiceProfileId is null)
            throw new InvalidOperationException($"角色“{line.Speaker}”尚未绑定音色配置。");

        var voiceProfile = await db.VoiceProfiles.FindAsync([character.VoiceProfileId.Value], cancellationToken)
                           ?? throw new InvalidOperationException($"角色“{line.Speaker}”绑定的音色不存在。");

        var output = $"storage/audio/{line.NovelId}/{line.Order:D6}.wav";
        line.Status = "Generating";
        await db.SaveChangesAsync(cancellationToken);

        await tts.GenerateAsync(line.Text, voiceProfile, output, cancellationToken);

        line.AudioFile = output;
        line.Status = "Completed";
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task MergeAudioAsync(
        IServiceProvider services,
        AppDbContext db,
        JobRecord job,
        long novelId,
        CancellationToken cancellationToken)
    {
        var tts = services.GetRequiredService<ITtsClient>();
        var lines = await db.ScriptLines
            .Where(x => x.NovelId == novelId && x.Status == "Completed" && x.AudioFile != null)
            .OrderBy(x => x.Order)
            .ToListAsync(cancellationToken);

        var files = lines
            .Select(x => x.AudioFile!)
            .Where(File.Exists)
            .ToList();

        if (files.Count == 0)
            throw new InvalidOperationException("当前小说没有可合并的已生成音频片段。");

        job.TotalSteps = 1;
        job.Progress = 30;
        JobTimingCalculator.Refresh(job);
        await db.SaveChangesAsync(cancellationToken);

        job.Result = await tts.MergeToMp3Async(
            files,
            $"storage/output/novel-{novelId}.mp3",
            cancellationToken);

        job.Checkpoint = 1;
        job.Progress = 100;
        JobTimingCalculator.Refresh(job);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task LearnWritingStyleAsync(
        IServiceProvider services,
        AppDbContext db,
        JobRecord job,
        long novelId,
        CancellationToken cancellationToken)
    {
        var ai = services.GetRequiredService<IAiChatClient>();
        var novel = await db.Novels.FindAsync([novelId], cancellationToken)
                    ?? throw new InvalidOperationException("小说不存在。");

        var chunkText = await db.Settings
            .Where(x => x.Key == "AiChunkSize")
            .Select(x => x.Value)
            .FirstOrDefaultAsync(cancellationToken) ?? "12000";

        var chunkSize = int.TryParse(chunkText, out var parsed)
            ? Math.Clamp(parsed, 2000, 50000)
            : 12000;

        var chunks = Split(novel.Content, chunkSize).ToList();
        job.TotalSteps = chunks.Count + 1;

        var partials = new List<string>();
        if (job.Checkpoint > 0 && !string.IsNullOrWhiteSpace(job.Result))
        {
            try
            {
                partials = JsonSerializer.Deserialize<List<string>>(job.Result) ?? [];
            }
            catch
            {
                partials = [];
                job.Checkpoint = 0;
            }
        }

        var startIndex = Math.Min(job.Checkpoint, chunks.Count);

        for (var index = startIndex; index < chunks.Count; index++)
        {
            await EnsureNotStoppingAsync(db, job, cancellationToken);

            var result = await ai.ChatTrackedAsync(
                "你是小说写作技法研究专家。请只分析写作技法，不复述大段原文。",
                "请分析本片段的叙事视角、语言风格、章节节奏、人物塑造、对白方式、悬念设计、情绪推进、句式特点和可复用写作规则。\n\n" + chunks[index],
                new AiCallContext(
                    novelId,
                    job.Id,
                    "LearnWritingStyleChunk",
                    index + 1,
                    chunks.Count),
                cancellationToken);

            partials.Add(result);
            job.Result = JsonSerializer.Serialize(partials);
            job.Checkpoint = index + 1;
            job.Progress = (int)Math.Round((index + 1) * 90d / Math.Max(chunks.Count, 1));
            JobTimingCalculator.Refresh(job);
            await db.SaveChangesAsync(cancellationToken);
        }

        await EnsureNotStoppingAsync(db, job, cancellationToken);

        var synthesis = await ai.ChatTrackedAsync(
            "你是小说写作方法论专家。根据多段分析结果，整理成一个稳定、可复用、可指导新小说生成的写作风格模型。",
            "请生成：1. 风格总览；2. 叙事视角；3. 语言与句式；4. 节奏；5. 人物塑造；6. 对白；7. 悬念；8. 情绪推进；9. 禁忌；10. 可直接给小说生成模型使用的完整提示词模板。\n\n" +
            string.Join("\n\n--- 分块分析 ---\n", partials),
            new AiCallContext(
                novelId,
                job.Id,
                "LearnWritingStyleSynthesis",
                chunks.Count + 1,
                chunks.Count + 1),
            cancellationToken);

        var style = new WritingStyle
        {
            NovelId = novelId,
            Name = novel.Title + " · 风格模型",
            Summary = synthesis,
            PromptTemplate = synthesis
        };

        db.WritingStyles.Add(style);
        job.Checkpoint = chunks.Count + 1;
        job.Progress = 100;
        JobTimingCalculator.Refresh(job);
        await db.SaveChangesAsync(cancellationToken);

        job.Result = $"style:{style.Id}";
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureNotStoppingAsync(
        AppDbContext db,
        JobRecord job,
        CancellationToken cancellationToken)
    {
        await db.Entry(job).ReloadAsync(cancellationToken);
        if (job.Status == "Stopping")
            throw new OperationCanceledException("任务已由用户停止。");
    }

    private static IEnumerable<string> Split(string text, int chunkSize)
    {
        for (var index = 0; index < text.Length; index += chunkSize)
            yield return text.Substring(index, Math.Min(chunkSize, text.Length - index));
    }
}