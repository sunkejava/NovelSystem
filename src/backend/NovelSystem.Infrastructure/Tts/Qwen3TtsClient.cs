using System.Diagnostics;
using System.Net.Http.Json;
using NovelSystem.Application.Contracts;
using NovelSystem.Infrastructure.Persistence;
using NovelSystem.Infrastructure.Services;
namespace NovelSystem.Infrastructure.Tts;
/// <summary>Qwen3-TTS HTTP 适配器和 FFmpeg 合并实现。</summary>
public sealed class Qwen3TtsClient(IHttpClientFactory httpClientFactory,AppDbContext db):ITtsClient
{
    public async Task<string> GenerateAsync(string text,string? voiceFile,string outputFile,CancellationToken cancellationToken=default)
    {
        var settings=new SettingReader(db);var baseUrl=settings.Get("TtsBaseUrl","http://127.0.0.1:7860").TrimEnd('/');var endpoint=settings.Get("TtsEndpoint","/api/tts").TrimStart('/');
        using var client=httpClientFactory.CreateClient();using var response=await client.PostAsJsonAsync($"{baseUrl}/{endpoint}",new{text,ref_audio=voiceFile,language="zh"},cancellationToken);
        if(!response.IsSuccessStatusCode) throw new InvalidOperationException($"Qwen3-TTS 调用失败：{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);await File.WriteAllBytesAsync(outputFile,await response.Content.ReadAsByteArrayAsync(cancellationToken),cancellationToken);return outputFile;
    }
    public async Task<string> MergeToMp3Async(IEnumerable<string> inputFiles,string outputFile,CancellationToken cancellationToken=default)
    {
        var files=inputFiles.ToList();if(files.Count==0) throw new InvalidOperationException("没有可合并的音频片段。");
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);var concatFile=Path.ChangeExtension(outputFile,".concat.txt");await File.WriteAllLinesAsync(concatFile,files.Select(x=>$"file '{Path.GetFullPath(x)}'"),cancellationToken);
        var startInfo=new ProcessStartInfo{FileName=new SettingReader(db).Get("FfmpegPath","ffmpeg"),Arguments=$"-y -f concat -safe 0 -i \"{concatFile}\" -c:a libmp3lame -q:a 2 \"{outputFile}\"",RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
        using var process=Process.Start(startInfo)??throw new InvalidOperationException("无法启动 FFmpeg。");await process.WaitForExitAsync(cancellationToken);if(process.ExitCode!=0) throw new InvalidOperationException(await process.StandardError.ReadToEndAsync(cancellationToken));return outputFile;
    }
}