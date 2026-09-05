using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Application.Contracts;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Infrastructure.Tts;

/// <summary>
/// TTS 发音词典装饰器。保持 ScriptLine.Text 原文不变，只在真正送入 TTS 前做读音替换。
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
                spokenText = spokenText.Replace(
                    entry.Pattern,
                    entry.Replacement,
                    StringComparison.Ordinal);
            }
        }

        return await inner.GenerateAsync(spokenText, voiceProfile, outputFile, cancellationToken);
    }

    public Task<string> CreatePromptAsync(VoiceProfile voiceProfile, CancellationToken cancellationToken = default)
        => inner.CreatePromptAsync(voiceProfile, cancellationToken);

    public Task<string> MergeToMp3Async(IEnumerable<string> inputFiles, string outputFile, CancellationToken cancellationToken = default)
        => inner.MergeToMp3Async(inputFiles, outputFile, cancellationToken);

    private static long? TryGetNovelId(string outputFile)
    {
        var normalized = outputFile.Replace('\\', '/');
        var match = AudioPathRegex().Match(normalized);
        return match.Success && long.TryParse(match.Groups["id"].Value, out var id) ? id : null;
    }

    [GeneratedRegex(@"(?:^|/)storage/audio/(?<id>\d+)/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AudioPathRegex();
}
