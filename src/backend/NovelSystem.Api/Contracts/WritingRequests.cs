namespace NovelSystem.Api.Contracts;

/// <summary>修改写作风格名称、摘要及提示词模板。</summary>
public sealed record UpdateWritingStyleRequest(
    string Name,
    string Summary,
    string PromptTemplate);