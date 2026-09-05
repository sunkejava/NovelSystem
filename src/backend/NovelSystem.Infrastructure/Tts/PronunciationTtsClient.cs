using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Application.Contracts;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Persistence;
using NovelSystem.Infrastructure.Services;

namespace NovelSystem.Infrastructure.Tts;

/// <summary>
/// TTS 发音词典装饰器。保持 ScriptLine.Text 原文不变，只在真正送入 TTS 前做读音替换。
/// 同时对标准脚本音频路径执行 ffprobe，写入片段精确时长和累计时间轴。
/// </summary>
public sealed partial class PronunciationTtsClient(
    Qwen3TtsClient inner,
    AppDbContext db) : ITtsClient
{
    public async Task<string> GenerateAsync(
        string text,
        VoiceProfile voiceProfile,
        string outputFile,
        CancellationToken cancellationToken = default)
    {
        var spokenText = text;
        var novelId = TryGetNovelId(outputFile);
        if (novelId.HasValue)
        {
            var entries = await db.PronunciationEntries.AsNoTracking()
                .Where(x => x.NovelId == novelId.Value && x.IsEnabled)
                .OrderByDescending(x => x.Pattern.Length)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Pattern)) continue;
                spokenText = spokenText.Replace(entry.Pattern, entry.Replacement, StringComparison.Ordinal);
            }
        }

        var result = await inner.GenerateAsync(spokenText, voiceProfile, outputFile, cancellationToken);
        await TryUpdateTimelineAsync(outputFile, cancellationToken);
        return result;
    }

    public Task<string> CreatePromptAsync(VoiceProfile voiceProfile, CancellationToken cancellationToken = default)
        => inner.CreatePromptAsync(voiceProfile, cancellationToken);

    public Task<string> MergeToMp3Async(IEnumerable<string> inputFiles, string outputFile, CancellationToken cancellationToken = default)
        => inner.MergeToMp3Async(inputFiles, outputFile, cancellationToken);

    private async Task TryUpdateTimelineAsync(string outputFile, CancellationToken cancellationToken)
    {
        var normalized = outputFile.Replace('\\', '/');
        var match = CanonicalAudioRegex().Match(normalized);
        if (!match.Success ||
            !long.TryParse(match.Groups["id"].Value, out var novelId) ||
            !int.TryParse(match.Groups["order"].Value, out var order))
            return;

        var line = await db.ScriptLines.FirstOrDefaultAsync(x => x.NovelId == novelId && x.Order == order, cancellationToken);
        if (line is null) return;
        var duration = await AudioTimelineService.ProbeDurationMsAsync(db, outputFile, cancellationToken);
        if (!duration.HasValue) return;

        var previousEnd = await db.ScriptLines.AsNoTracking()
            .Where(x => x.NovelId == novelId && x.Order < order && x.AudioEndMs != null)
            .MaxAsync(x => (long?)x.AudioEndMs, cancellationToken) ?? 0;

        line.AudioFile = outputFile;
        line.AudioStartMs = previousEnd;
        line.AudioEndMs = previousEnd + duration.Value;

        // 单段重生成可能改变时长。后续片段的累计位置必须重新计算，先清空避免显示错误时间。
        await db.ScriptLines
            .Where(x => x.NovelId == novelId && x.Order > order && x.AudioStartMs != null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.AudioStartMs, (long?)null)
                .SetProperty(x => x.AudioEndMs, (long?)null), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static long? TryGetNovelId(string outputFile)
    {
        var normalized = outputFile.Replace('\\', '/');
        var match = AudioPathRegex().Match(normalized);
        return match.Success && long.TryParse(match.Groups["id"].Value, out var id) ? id : null;
    }

    [GeneratedRegex(@"(?:^|/)storage/audio/(?<id>\d+)/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AudioPathRegex();

    [GeneratedRegex(@"(?:^|/)storage/audio/(?<id>\d+)/(?<order>\d+)\.wav$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalAudioRegex();
}
