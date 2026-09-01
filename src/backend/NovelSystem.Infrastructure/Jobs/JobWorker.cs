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

                case "GenerateNovel":
                    await GenerateNovelAsync(
                        scope.ServiceProvider,
                        db,
                        job,
                        payload.RootElement,
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
        VoiceProfile? voiceProfile;

        if (line.CharacterId is null || string.Equals(line.Speaker, "旁白", StringComparison.OrdinalIgnoreCase))
        {
            var narratorVoiceProfileId = await db.Novels.AsNoTracking()
                .Where(x => x.Id == line.NovelId)
                .Select(x => x.NarratorVoiceProfileId)
                .SingleOrDefaultAsync(cancellationToken);

            if (!narratorVoiceProfileId.HasValue)
                throw new InvalidOperationException("当前小说尚未配置旁白音色，请在“角色声纹矩阵”中为旁白绑定音色。");

            voiceProfile = await db.VoiceProfiles.FindAsync([narratorVoiceProfileId.Value], cancellationToken)
                           ?? throw new InvalidOperationException("当前小说绑定的旁白音色档案不存在，请重新选择旁白音色。");
        }
        else
        {
            var character = await db.Characters.FindAsync([line.CharacterId.Value], cancellationToken);
            if (character?.VoiceProfileId is null)
                throw new InvalidOperationException($"角色“{line.Speaker}”尚未绑定音色配置。");

            voiceProfile = await db.VoiceProfiles.FindAsync([character.VoiceProfileId.Value], cancellationToken)
                           ?? throw new InvalidOperationException($"角色“{line.Speaker}”绑定的音色不存在。");
        }

        var output = $"storage/audio/{line.NovelId}/{line.Order:D6}.wav";
        line.Status = "Generating";
        await db.SaveChangesAsync(cancellationToken);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await tts.GenerateAsync(line.Text, voiceProfile, output, cancellationToken);
            stopwatch.Stop();

            var estimatedTokens = EstimateTextTokens(line.Text);
            db.AiTokenUsages.Add(new AiTokenUsage
            {
                NovelId = line.NovelId,
                Operation = "TtsGenerate",
                Model = "Qwen3-TTS",
                PromptTokens = estimatedTokens,
                CompletionTokens = 0,
                TotalTokens = estimatedTokens,
                InputCharacters = line.Text.Length,
                OutputCharacters = 0,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                IsEstimated = true,
                Success = true,
                CreatedAt = DateTime.UtcNow
            });

            line.AudioFile = output;
            line.Status = "Completed";
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var estimatedTokens = EstimateTextTokens(line.Text);
            db.AiTokenUsages.Add(new AiTokenUsage
            {
                NovelId = line.NovelId,
                Operation = "TtsGenerate",
                Model = "Qwen3-TTS",
                PromptTokens = estimatedTokens,
                TotalTokens = estimatedTokens,
                InputCharacters = line.Text.Length,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                IsEstimated = true,
                Success = false,
                Error = ex.Message,
                CreatedAt = DateTime.UtcNow
            });
            line.Status = "Failed";
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
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

        var settings = await db.Settings.AsNoTracking()
            .ToDictionaryAsync(x => x.Key, x => x.Value, cancellationToken);
        var chunkSize = int.TryParse(settings.GetValueOrDefault("AiStyleChunkSize", "16000"), out var parsedChunk)
            ? Math.Clamp(parsedChunk, 4000, 50000)
            : 16000;
        var maxSamples = int.TryParse(settings.GetValueOrDefault("AiStyleSampleChunks", "12"), out var parsedSamples)
            ? Math.Clamp(parsedSamples, 4, 40)
            : 12;

        var allChunks = Split(novel.Content, chunkSize).ToList();
        var selected = SelectRepresentativeChunks(allChunks, maxSamples);
        var reduceGroupSize = 4;
        var reduceSteps = (int)Math.Ceiling(selected.Count / (double)reduceGroupSize);
        job.TotalSteps = selected.Count + reduceSteps + 1;
        job.Checkpoint = 0;
        job.Progress = 0;
        job.Result = null;
        JobTimingCalculator.Refresh(job);
        await db.SaveChangesAsync(cancellationToken);

        var partials = new List<string>(selected.Count);
        for (var index = 0; index < selected.Count; index++)
        {
            await EnsureNotStoppingAsync(db, job, cancellationToken);
            var result = await ai.ChatTrackedAsync(
                "你是小说写作技法研究专家。只提取可复用的写作规律，不复述剧情，不引用长原文。",
                """
                分析本片段的：叙事视角、语言与句式、章节/场景节奏、人物塑造、对白方式、
                悬念与信息释放、情绪推进、描写密度、常用转场、禁忌与可复用规则。
                每项尽量用简洁规则表达，整体控制在1200字以内。

                小说样本：
                """ + selected[index],
                new AiCallContext(novelId, job.Id, "LearnWritingStyleChunk", index + 1, selected.Count),
                cancellationToken);

            partials.Add(result);
            job.Checkpoint++;
            job.Progress = (int)Math.Round(job.Checkpoint * 100d / job.TotalSteps);
            job.Result = JsonSerializer.Serialize(partials);
            JobTimingCalculator.Refresh(job);
            await db.SaveChangesAsync(cancellationToken);
        }

        var reduced = new List<string>();
        for (var groupIndex = 0; groupIndex < partials.Count; groupIndex += reduceGroupSize)
        {
            await EnsureNotStoppingAsync(db, job, cancellationToken);
            var group = partials.Skip(groupIndex).Take(reduceGroupSize).ToList();
            var result = await ai.ChatTrackedAsync(
                "你是小说风格研究员。合并多段局部分析，去重并保留稳定、反复出现的写作规律。",
                "请压缩为不超过1800字的风格规则摘要：\n\n" + string.Join("\n\n---\n", group),
                new AiCallContext(novelId, job.Id, "LearnWritingStyleReduce", reduced.Count + 1, reduceSteps),
                cancellationToken);

            reduced.Add(result);
            job.Checkpoint++;
            job.Progress = (int)Math.Round(job.Checkpoint * 100d / job.TotalSteps);
            JobTimingCalculator.Refresh(job);
            await db.SaveChangesAsync(cancellationToken);
        }

        await EnsureNotStoppingAsync(db, job, cancellationToken);
        var synthesis = await ai.ChatTrackedAsync(
            "你是小说写作方法论专家。根据代表性样本的风格摘要，生成稳定、可复用、可指导原创小说生成的写作风格模型。",
            """
            请生成：1.风格总览；2.叙事视角；3.语言与句式；4.节奏与章节结构；
            5.人物塑造；6.对白；7.悬念与信息释放；8.情绪推进；9.描写与转场；
            10.禁忌；11.可直接给小说生成模型使用的完整提示词模板。

            风格摘要：
            """ + string.Join("\n\n=== REDUCED STYLE ===\n", reduced),
            new AiCallContext(novelId, job.Id, "LearnWritingStyleSynthesis", 1, 1),
            cancellationToken);

        var style = new WritingStyle
        {
            NovelId = novelId,
            Name = novel.Title + " · 风格模型",
            Summary = synthesis,
            PromptTemplate = synthesis
        };

        db.WritingStyles.Add(style);
        job.Checkpoint = job.TotalSteps;
        job.Progress = 100;
        JobTimingCalculator.Refresh(job);
        await db.SaveChangesAsync(cancellationToken);

        job.Result = $"style:{style.Id}";
        await db.SaveChangesAsync(cancellationToken);
    }

    private static List<string> SelectRepresentativeChunks(IReadOnlyList<string> chunks, int maxSamples)
    {
        if (chunks.Count <= maxSamples)
            return chunks.ToList();

        var result = new List<string>(maxSamples);
        var used = new HashSet<int>();
        for (var i = 0; i < maxSamples; i++)
        {
            var position = i * (chunks.Count - 1d) / (maxSamples - 1d);
            var index = (int)Math.Round(position);
            if (used.Add(index))
                result.Add(chunks[index]);
        }
        return result;
    }

    private static int EstimateTextTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var cjk = text.Count(ch => ch >= 0x2E80);
        var other = Math.Max(0, text.Length - cjk);
        return cjk + (int)Math.Ceiling(other / 4d);
    }

    /// <summary>
    /// 长篇 AI 创作任务。先生成全局创作蓝图，再按章节逐章生成并实时落库。
    /// 章节和目标字数不设置业务上限；每完成一章即推进 checkpoint/progress，
    /// 失败重试时从最后完成章节继续，不重复生成已完成内容。
    /// </summary>
    private static async Task GenerateNovelAsync(
        IServiceProvider services,
        AppDbContext db,
        JobRecord job,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var ai = services.GetRequiredService<IAiChatClient>();

        var generatedNovelId = payload.GetProperty("generatedNovelId").GetInt64();
        var generated = await db.GeneratedNovels.FindAsync([generatedNovelId], cancellationToken)
                        ?? throw new InvalidOperationException("AI 创作记录不存在。");

        var styleId = payload.TryGetProperty("StyleId", out var styleIdProperty) &&
                      styleIdProperty.ValueKind == JsonValueKind.Number
            ? styleIdProperty.GetInt64()
            : generated.StyleId;

        var style = styleId.HasValue
            ? await db.WritingStyles.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == styleId.Value, cancellationToken)
            : null;

        var title = payload.TryGetProperty("Title", out var titleProperty)
            ? titleProperty.GetString() ?? generated.Title
            : generated.Title;
        var prompt = payload.TryGetProperty("Prompt", out var promptProperty)
            ? promptProperty.GetString() ?? generated.Prompt
            : generated.Prompt;
        var genre = payload.TryGetProperty("Genre", out var genreProperty)
            ? genreProperty.GetString()
            : generated.Genre;
        var pointOfView = payload.TryGetProperty("PointOfView", out var povProperty)
            ? povProperty.GetString()
            : generated.PointOfView;
        var tone = payload.TryGetProperty("Tone", out var toneProperty)
            ? toneProperty.GetString()
            : generated.Tone;

        var targetWords = payload.TryGetProperty("TargetWords", out var targetWordsProperty)
            ? targetWordsProperty.GetInt32()
            : generated.TargetWords;
        var chapterCount = payload.TryGetProperty("ChapterCount", out var chapterCountProperty)
            ? chapterCountProperty.GetInt32()
            : generated.ChapterCount;

        if (targetWords <= 0 || chapterCount <= 0)
            throw new InvalidOperationException("目标字数和章节数必须大于 0。");

        job.TotalSteps = chapterCount + 1;
        var constraints = $"""
            小说标题：{title}
            题材：{genre ?? "不限"}
            总目标字数：约 {targetWords:N0} 字
            总章节数：{chapterCount:N0}
            叙事视角：{pointOfView ?? "自动选择"}
            整体基调：{tone ?? "按故事需要"}
            用户创作要求：{prompt}
            """;

        // checkpoint=0 表示大纲尚未完成；>=1 表示大纲已完成。
        if (job.Checkpoint <= 0 || string.IsNullOrWhiteSpace(generated.Outline))
        {
            await EnsureNotStoppingAsync(db, job, cancellationToken);

            generated.Outline = await ai.ChatTrackedAsync(
                "你是中文长篇小说总编剧。制定可长期维持人物一致性、冲突升级和伏笔回收的原创创作蓝图。",
                (style?.PromptTemplate ?? string.Empty) +
                """
                
                请先制定全书创作蓝图。不要直接写正文。
                当章节很多时不要逐章展开到很长，而是用“故事阶段/篇章弧 + 关键章节节点”的方式规划，
                明确主角目标、主要人物关系、核心冲突、阶段升级、关键伏笔、转折点、高潮与结局方向。
                蓝图必须能指导后续逐章生成并保持前后连续。
                
                """ + constraints,
                new AiCallContext(generated.SourceNovelId, job.Id, "GenerateNovelOutline"),
                cancellationToken);

            job.Checkpoint = 1;
            job.Progress = (int)Math.Round(100d / job.TotalSteps);
            JobTimingCalculator.Refresh(job);
            await db.SaveChangesAsync(cancellationToken);
        }

        var completedChapters = Math.Max(0, job.Checkpoint - 1);
        var wordsPerChapter = Math.Max(1L, (long)Math.Ceiling(targetWords / (double)chapterCount));

        for (var chapterIndex = completedChapters; chapterIndex < chapterCount; chapterIndex++)
        {
            await EnsureNotStoppingAsync(db, job, cancellationToken);

            var chapterNumber = chapterIndex + 1;
            var previousTail = string.IsNullOrWhiteSpace(generated.Content)
                ? "这是第一章，无前文。"
                : generated.Content.Length <= 5000
                    ? generated.Content
                    : generated.Content[^5000..];

            var chapter = await ai.ChatTrackedAsync(
                "你是专业中文长篇小说作者。只输出当前章节正文，不解释写作过程，不提前生成后续章节。",
                (style?.PromptTemplate ?? string.Empty) +
                $"""
                
                {constraints}
                
                全书创作蓝图：
                {generated.Outline}
                
                当前任务：
                生成第 {chapterNumber:N0} / {chapterCount:N0} 章。
                本章目标长度约 {wordsPerChapter:N0} 字；这是目标值而不是硬截断点，应优先保证章节完整性。
                必须承接已有正文，人物姓名、性格、能力、关系、时间线和已发生事件保持一致。
                本章末尾应自然形成推进下一章的动力，但不要输出下一章内容。
                
                最近正文上下文：
                {previousTail}
                """,
                new AiCallContext(
                    generated.SourceNovelId,
                    job.Id,
                    "GenerateNovelChapter",
                    chapterNumber,
                    chapterCount),
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(generated.Content))
                generated.Content += Environment.NewLine + Environment.NewLine;

            generated.Content += $"第{chapterNumber}章" + Environment.NewLine + chapter.Trim();

            job.Checkpoint = chapterNumber + 1;
            job.Progress = (int)Math.Round(job.Checkpoint * 100d / job.TotalSteps);
            job.Result = $"generated:{generated.Id}";
            JobTimingCalculator.Refresh(job);

            // 每章完成立即落库：前端可实时看到进度，失败/停止后也保留已完成正文。
            await db.SaveChangesAsync(cancellationToken);
        }

        job.Progress = 100;
        job.Checkpoint = job.TotalSteps;
        job.Result = $"generated:{generated.Id}";
        JobTimingCalculator.Refresh(job);
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