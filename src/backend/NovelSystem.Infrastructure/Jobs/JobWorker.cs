using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NovelSystem.Application.Contracts;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Infrastructure.Jobs;

/// <summary>执行小说解析、TTS 音频生成，并在服务启动时恢复未完成任务。</summary>
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
        var pending = await db.Jobs.Where(x => x.Status == "Queued" || x.Status == "Running" || x.Status == "Stopping").ToListAsync(cancellationToken);

        foreach (var job in pending.Where(x => !string.IsNullOrWhiteSpace(x.Payload)))
        {
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
        if (job is null || job.Status == "Stopped") return;
        if (job.Status == "Stopping")
        {
            job.Status = "Stopped";
            job.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            job.Status = "Running";
            job.StartedAt ??= DateTime.UtcNow;
            job.Error = null;
            await db.SaveChangesAsync(cancellationToken);

            var novelId = JsonDocument.Parse(message.Payload).RootElement.GetProperty("novelId").GetInt64();

            if (message.Type == "AnalyzeNovel")
                await scope.ServiceProvider.GetRequiredService<INovelAnalysisService>()
                    .AnalyzeAsync(novelId, job.Id, cancellationToken);
            else if (message.Type == "GenerateAudio")
                await GenerateAudioAsync(scope.ServiceProvider, db, job, novelId, cancellationToken);

            job.Status = "Completed";
            job.Progress = 100;
            job.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException ex) when (ex.Message.Contains("用户停止"))
        {
            job.Status = "Stopped";
            job.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            job.Status = "Failed";
            job.Error = ex.ToString();
            job.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
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
        var lines = await db.ScriptLines.Where(x => x.NovelId == novelId).OrderBy(x => x.Order).ToListAsync(cancellationToken);
        if (lines.Count == 0)
            throw new InvalidOperationException("请先完成小说 AI 解析。");

        var files = new List<string>();

        for (var index = 0; index < lines.Count; index++)
        {
            await db.Entry(job).ReloadAsync(cancellationToken);
            if (job.Status == "Stopping")
                throw new OperationCanceledException("任务已由用户停止。");

            var line = lines[index];

            // 继续任务时复用之前已经生成成功的片段。
            if (line.Status == "Completed" && !string.IsNullOrWhiteSpace(line.AudioFile) && File.Exists(line.AudioFile))
            {
                files.Add(line.AudioFile);
                job.Progress = (int)Math.Round((index + 1) * 90d / lines.Count);
                continue;
            }

            var character = line.CharacterId is null
                ? null
                : await db.Characters.FindAsync([line.CharacterId.Value], cancellationToken);

            if (character?.VoiceProfileId is null)
                throw new InvalidOperationException($"角色“{line.Speaker}”尚未绑定音色配置。");

            var voiceProfile = await db.VoiceProfiles.FindAsync([character.VoiceProfileId.Value], cancellationToken)
                               ?? throw new InvalidOperationException($"角色“{line.Speaker}”绑定的音色不存在。");

            var output = $"storage/audio/{novelId}/{line.Order:D6}.wav";
            await tts.GenerateAsync(line.Text, voiceProfile, output, cancellationToken);

            line.AudioFile = output;
            line.Status = "Completed";
            files.Add(output);
            job.Progress = (int)Math.Round((index + 1) * 90d / lines.Count);
            await db.SaveChangesAsync(cancellationToken);
        }

        job.Result = await tts.MergeToMp3Async(files, $"storage/output/novel-{novelId}.mp3", cancellationToken);
    }
}