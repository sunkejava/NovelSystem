using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Infrastructure.Services;

/// <summary>
/// 使用 ffprobe 读取单段音频精确时长，并根据脚本顺序计算整本有声书的累计时间轴。
/// </summary>
public static class AudioTimelineService
{
    public static async Task<long?> ProbeDurationMsAsync(
        AppDbContext db,
        string? audioFile,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(audioFile))
            return null;

        var fullPath = Path.GetFullPath(audioFile);
        if (!File.Exists(fullPath))
            return null;

        var ffprobe = await db.Settings.AsNoTracking()
            .Where(x => x.Key == "FfprobePath")
            .Select(x => x.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(ffprobe))
        {
            var ffmpeg = await db.Settings.AsNoTracking()
                .Where(x => x.Key == "FfmpegPath")
                .Select(x => x.Value)
                .FirstOrDefaultAsync(cancellationToken) ?? "ffmpeg";
            ffprobe = DeriveFfprobePath(ffmpeg);
        }

        var psi = new ProcessStartInfo
        {
            FileName = ffprobe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-show_entries");
        psi.ArgumentList.Add("format=duration");
        psi.ArgumentList.Add("-of");
        psi.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
        psi.ArgumentList.Add(fullPath);

        using var process = Process.Start(psi);
        if (process is null)
            return null;

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await outputTask).Trim();
        _ = await errorTask;

        if (process.ExitCode != 0 ||
            !double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
            seconds < 0)
            return null;

        return Math.Max(0L, (long)Math.Round(seconds * 1000d));
    }

    public static async Task RecalculateNovelTimelineAsync(
        AppDbContext db,
        long novelId,
        CancellationToken cancellationToken = default)
    {
        var lines = await db.ScriptLines
            .Where(x => x.NovelId == novelId)
            .OrderBy(x => x.Order)
            .ToListAsync(cancellationToken);

        long cursor = 0;
        foreach (var line in lines)
        {
            if (line.Status != "Completed" || string.IsNullOrWhiteSpace(line.AudioFile) || !File.Exists(Path.GetFullPath(line.AudioFile)))
            {
                line.AudioStartMs = null;
                line.AudioEndMs = null;
                continue;
            }

            var duration = await ProbeDurationMsAsync(db, line.AudioFile, cancellationToken);
            if (!duration.HasValue)
            {
                line.AudioStartMs = null;
                line.AudioEndMs = null;
                continue;
            }

            line.AudioStartMs = cursor;
            line.AudioEndMs = cursor + duration.Value;
            cursor = line.AudioEndMs.Value;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string DeriveFfprobePath(string ffmpegPath)
    {
        var fileName = Path.GetFileName(ffmpegPath);
        if (fileName.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(Path.GetDirectoryName(ffmpegPath) ?? string.Empty, "ffprobe.exe");
        if (fileName.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(Path.GetDirectoryName(ffmpegPath) ?? string.Empty, "ffprobe");
        return "ffprobe";
    }
}
