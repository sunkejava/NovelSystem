using NovelSystem.Domain.Entities;

namespace NovelSystem.Application.Contracts;

/// <summary>
/// TTS 服务抽象。Qwen3-TTS 实现支持 Gradio upload/call/SSE 三阶段协议。
/// </summary>
public interface ITtsClient
{
    Task<string> GenerateAsync(string text, VoiceProfile voiceProfile, string outputFile, CancellationToken cancellationToken = default);
    Task<string> CreatePromptAsync(VoiceProfile voiceProfile, CancellationToken cancellationToken = default);
    Task<string> MergeToMp3Async(IEnumerable<string> inputFiles, string outputFile, CancellationToken cancellationToken = default);
}