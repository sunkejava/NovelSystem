using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Infrastructure.Services;

/// <summary>专业制作多轨混音导出。</summary>
public static class ProductionMixService
{
    public static async Task<string> MixAsync(AppDbContext db, long novelId, CancellationToken cancellationToken)
    {
        var voiceFile = Path.GetFullPath($"storage/output/novel-{novelId}.mp3");
        if (!File.Exists(voiceFile))
            throw new InvalidOperationException("请先生成并合并完整有声书 MP3，才能进行多轨混音。");

        var tracks = await db.ProductionTracks.AsNoTracking()
            .Where(x => x.NovelId == novelId && !x.IsMuted)
            .OrderBy(x => x.Order)
            .ToListAsync(cancellationToken);
        var trackIds = tracks.Select(x => x.Id).ToList();
        var clips = await db.ProductionTrackClips.AsNoTracking()
            .Where(x => x.NovelId == novelId && trackIds.Contains(x.TrackId))
            .OrderBy(x => x.StartMs)
            .ToListAsync(cancellationToken);
        var trackMap = tracks.ToDictionary(x => x.Id);

        var settings = await db.Settings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value, cancellationToken);
        var ffmpeg = settings.GetValueOrDefault("FfmpegPath", "ffmpeg");
        var output = Path.GetFullPath($"storage/output/novel-{novelId}-mixed.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(voiceFile);

        var validClips = new List<(Domain.Entities.ProductionTrackClip Clip, Domain.Entities.ProductionTrack Track)>();
        foreach (var clip in clips)
        {
            var path = Path.GetFullPath(clip.FilePath);
            if (!File.Exists(path)) continue;
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(path);
            validClips.Add((clip, trackMap[clip.TrackId]));
        }

        if (validClips.Count == 0)
        {
            File.Copy(voiceFile, output, true);
            return output;
        }

        var filters = new List<string> { "[0:a]volume=1[a0]" };
        var mixLabels = new List<string> { "[a0]" };
        for (var i = 0; i < validClips.Count; i++)
        {
            var inputIndex = i + 1;
            var (clip, track) = validClips[i];
            var label = $"a{inputIndex}";
            var volume = Math.Clamp(track.Volume * clip.Volume, 0d, 4d).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var chain = $"[{inputIndex}:a]volume={volume}";
            if (clip.FadeInMs > 0)
                chain += $",afade=t=in:st=0:d={(clip.FadeInMs / 1000d).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}";
            if (clip.FadeOutMs > 0 && clip.DurationMs > clip.FadeOutMs)
            {
                var fadeStart = (clip.DurationMs - clip.FadeOutMs) / 1000d;
                chain += $",afade=t=out:st={fadeStart.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}:d={(clip.FadeOutMs / 1000d).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}";
            }
            chain += $",adelay={clip.StartMs}|{clip.StartMs}[{label}]";
            filters.Add(chain);
            mixLabels.Add($"[{label}]");
        }
        filters.Add(string.Concat(mixLabels) + $"amix=inputs={mixLabels.Count}:duration=longest:normalize=0[out]");

        startInfo.ArgumentList.Add("-filter_complex");
        startInfo.ArgumentList.Add(string.Join(';', filters));
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("[out]");
        startInfo.ArgumentList.Add("-c:a");
        startInfo.ArgumentList.Add("libmp3lame");
        startInfo.ArgumentList.Add("-q:a");
        startInfo.ArgumentList.Add("2");
        startInfo.ArgumentList.Add(output);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 FFmpeg 多轨混音进程。");
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg 多轨混音失败，ExitCode={process.ExitCode}: {error}");

        return output;
    }
}
