namespace NovelSystem.Application.Contracts;
/// <summary>小说 AI 解析用例。</summary>
public interface INovelAnalysisService { Task AnalyzeAsync(long novelId,long jobId,CancellationToken cancellationToken=default); }