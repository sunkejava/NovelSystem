namespace NovelSystem.Application.Contracts;
/// <summary>TTS 服务抽象，默认由 Qwen3-TTS 实现。</summary>
public interface ITtsClient { Task<string> GenerateAsync(string text,string? voiceFile,string outputFile,CancellationToken cancellationToken=default); Task<string> MergeToMp3Async(IEnumerable<string> inputFiles,string outputFile,CancellationToken cancellationToken=default); }