using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NovelSystem.Application.Contracts;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Persistence;
using NovelSystem.Infrastructure.Services;

namespace NovelSystem.Infrastructure.Tts;

/// <summary>
/// Qwen3-TTS Gradio API 客户端。
/// 完整支持：文件上传 -> 创建事件 -> SSE 获取结果 -> 下载结果文件。
/// 音色首次使用时会生成 Prompt 缓存，后续优先走 load_prompt_and_gen。
/// </summary>
public sealed class Qwen3TtsClient(IHttpClientFactory httpClientFactory, AppDbContext db) : ITtsClient
{
    public async Task<string> GenerateAsync(
        string text,
        VoiceProfile voiceProfile,
        string outputFile,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("TTS 文本不能为空。", nameof(text));

        // Prompt 模式避免每一句都重复做完整声音克隆，长篇小说性能会明显更好。
        if (string.IsNullOrWhiteSpace(voiceProfile.PromptFile) || !File.Exists(voiceProfile.PromptFile))
        {
            try
            {
                await CreatePromptAsync(voiceProfile, cancellationToken);
            }
            catch
            {
                // Prompt 生成失败时仍允许直接 Voice Clone，保证任务可继续运行。
                return await GenerateByVoiceCloneAsync(text, voiceProfile, outputFile, cancellationToken);
            }
        }

        return await GenerateByPromptAsync(text, voiceProfile, outputFile, cancellationToken);
    }

    public async Task<string> CreatePromptAsync(VoiceProfile voiceProfile, CancellationToken cancellationToken = default)
    {
        ValidateProfile(voiceProfile);
        var settings = new SettingReader(db);
        var promptDirectory = settings.Get("PromptDirectory", "storage/prompts");
        Directory.CreateDirectory(promptDirectory);

        var uploadedAudio = await UploadFileAsync(voiceProfile.ReferenceAudioFile, cancellationToken);
        var payload = new
        {
            ref_aud = FileData(uploadedAudio),
            ref_txt = voiceProfile.ReferenceText,
            use_xvec = voiceProfile.UseXVector
        };

        var result = await CallGradioAsync(
            settings.Get("TtsSavePromptSubmitEndpoint", "/gradio_api/call/v2/save_prompt"),
            settings.Get("TtsSavePromptResultEndpoint", "/gradio_api/call/save_prompt/{eventId}"),
            payload,
            cancellationToken);

        var target = Path.Combine(promptDirectory, $"voice-{voiceProfile.Id}-{SanitizeFileName(voiceProfile.Name)}.prompt");
        await DownloadGradioFileAsync(result, target, cancellationToken);

        voiceProfile.PromptFile = target;
        voiceProfile.Status = "PromptReady";
        voiceProfile.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return target;
    }

    public async Task<string> MergeToMp3Async(
        IEnumerable<string> inputFiles,
        string outputFile,
        CancellationToken cancellationToken = default)
    {
        var files = inputFiles.ToList();
        if (files.Count == 0)
            throw new InvalidOperationException("没有可合并的音频片段。");

        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
        var concatFile = Path.ChangeExtension(outputFile, ".concat.txt");
        await File.WriteAllLinesAsync(
            concatFile,
            files.Select(x => $"file '{Path.GetFullPath(x).Replace("'", "'\\''")}'"),
            cancellationToken);

        var startInfo = new ProcessStartInfo
        {
            FileName = new SettingReader(db).Get("FfmpegPath", "ffmpeg"),
            Arguments = $"-y -f concat -safe 0 -i \"{concatFile}\" -c:a libmp3lame -q:a 2 \"{outputFile}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 FFmpeg。");
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(await process.StandardError.ReadToEndAsync(cancellationToken));

        return outputFile;
    }

    private async Task<string> GenerateByPromptAsync(
        string text,
        VoiceProfile voiceProfile,
        string outputFile,
        CancellationToken cancellationToken)
    {
        var settings = new SettingReader(db);
        var uploadedPrompt = await UploadFileAsync(voiceProfile.PromptFile!, cancellationToken);
        var payload = new
        {
            file_obj = FileData(uploadedPrompt),
            text,
            lang_disp = string.IsNullOrWhiteSpace(voiceProfile.Language)
                ? settings.Get("TtsDefaultLanguage", "Chinese")
                : voiceProfile.Language
        };

        var result = await CallGradioAsync(
            settings.Get("TtsPromptGenSubmitEndpoint", "/gradio_api/call/v2/load_prompt_and_gen"),
            settings.Get("TtsPromptGenResultEndpoint", "/gradio_api/call/load_prompt_and_gen/{eventId}"),
            payload,
            cancellationToken);

        await DownloadGradioFileAsync(result, outputFile, cancellationToken);
        return outputFile;
    }

    private async Task<string> GenerateByVoiceCloneAsync(
        string text,
        VoiceProfile voiceProfile,
        string outputFile,
        CancellationToken cancellationToken)
    {
        ValidateProfile(voiceProfile);
        var settings = new SettingReader(db);
        var uploadedAudio = await UploadFileAsync(voiceProfile.ReferenceAudioFile, cancellationToken);

        var payload = new
        {
            ref_aud = FileData(uploadedAudio),
            ref_txt = voiceProfile.ReferenceText,
            use_xvec = voiceProfile.UseXVector,
            text,
            lang_disp = string.IsNullOrWhiteSpace(voiceProfile.Language)
                ? settings.Get("TtsDefaultLanguage", "Chinese")
                : voiceProfile.Language
        };

        var result = await CallGradioAsync(
            settings.Get("TtsVoiceCloneSubmitEndpoint", "/gradio_api/call/v2/run_voice_clone"),
            settings.Get("TtsVoiceCloneResultEndpoint", "/gradio_api/call/run_voice_clone/{eventId}"),
            payload,
            cancellationToken);

        await DownloadGradioFileAsync(result, outputFile, cancellationToken);
        return outputFile;
    }

    private async Task<string> UploadFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Qwen3-TTS 输入文件不存在。", filePath);

        var settings = new SettingReader(db);
        using var client = CreateClient();
        using var form = new MultipartFormDataContent();
        await using var stream = File.OpenRead(filePath);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "files", Path.GetFileName(filePath));

        var url = BuildUrl(settings.Get("TtsUploadEndpoint", "/gradio_api/upload"));
        using var response = await client.PostAsync(url, form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0)
            throw new InvalidOperationException($"Gradio 上传返回格式异常：{body}");

        return json.RootElement[0].GetString()
               ?? throw new InvalidOperationException("Gradio 上传未返回文件路径。");
    }

    private async Task<JsonElement> CallGradioAsync(
        string submitEndpoint,
        string resultEndpoint,
        object payload,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var submit = await client.PostAsJsonAsync(BuildUrl(submitEndpoint), payload, cancellationToken);
        var submitBody = await submit.Content.ReadAsStringAsync(cancellationToken);
        submit.EnsureSuccessStatusCode();

        using var submitJson = JsonDocument.Parse(submitBody);
        var eventId = submitJson.RootElement.TryGetProperty("event_id", out var eventProperty)
            ? eventProperty.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(eventId))
            throw new InvalidOperationException($"Gradio 未返回 event_id：{submitBody}");

        var resultUrl = BuildUrl(resultEndpoint.Replace("{eventId}", eventId, StringComparison.OrdinalIgnoreCase));
        using var request = new HttpRequestMessage(HttpMethod.Get, resultUrl);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? eventName = null;
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;

            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = line[6..].Trim();
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;

            var data = line[5..].Trim();
            if (eventName?.Equals("error", StringComparison.OrdinalIgnoreCase) == true)
                throw new InvalidOperationException($"Qwen3-TTS 任务失败：{data}");

            if (eventName?.Equals("complete", StringComparison.OrdinalIgnoreCase) == true)
            {
                using var resultJson = JsonDocument.Parse(data);
                if (resultJson.RootElement.ValueKind != JsonValueKind.Array || resultJson.RootElement.GetArrayLength() == 0)
                    throw new InvalidOperationException($"Qwen3-TTS 返回结果异常：{data}");

                return resultJson.RootElement[0].Clone();
            }
        }

        throw new TimeoutException("Qwen3-TTS SSE 连接结束但没有收到 complete 事件。");
    }

    private async Task DownloadGradioFileAsync(JsonElement fileResult, string targetFile, CancellationToken cancellationToken)
    {
        string? url = null;
        string? path = null;

        if (fileResult.ValueKind == JsonValueKind.Object)
        {
            if (fileResult.TryGetProperty("url", out var urlProperty))
                url = urlProperty.GetString();
            if (fileResult.TryGetProperty("path", out var pathProperty))
                path = pathProperty.GetString();
        }
        else if (fileResult.ValueKind == JsonValueKind.String)
        {
            path = fileResult.GetString();
        }

        if (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"Gradio 文件结果无法识别：{fileResult}");

        if (string.IsNullOrWhiteSpace(url))
            url = BuildUrl("/gradio_api/file=" + Uri.EscapeDataString(path!));
        else if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            url = BuildUrl(url);

        using var client = CreateClient();
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
        await File.WriteAllBytesAsync(targetFile, await response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken);
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient();
        var timeoutText = new SettingReader(db).Get("TtsTimeoutSeconds", "300");
        client.Timeout = TimeSpan.FromSeconds(int.TryParse(timeoutText, out var seconds) ? Math.Max(seconds, 60) : 300);
        return client;
    }

    private string BuildUrl(string endpoint)
    {
        var baseUrl = new SettingReader(db).Get("TtsBaseUrl", "http://127.0.0.1:8000").TrimEnd('/');
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var absolute))
            return absolute.ToString();
        return baseUrl + "/" + endpoint.TrimStart('/');
    }

    private static object FileData(string path) => new
    {
        path,
        meta = new Dictionary<string, string> { ["_type"] = "gradio.FileData" }
    };

    private static void ValidateProfile(VoiceProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ReferenceAudioFile))
            throw new InvalidOperationException("音色未配置参考 WAV。");
        if (!profile.UseXVector && string.IsNullOrWhiteSpace(profile.ReferenceText))
            throw new InvalidOperationException("当前音色未启用 x-vector，必须填写参考音频文本。");
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value;
    }
}