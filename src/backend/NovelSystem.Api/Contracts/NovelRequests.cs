namespace NovelSystem.Api.Contracts;

/// <summary>编辑小说资产请求。</summary>
public sealed record UpdateNovelRequest(string Title, string Content);