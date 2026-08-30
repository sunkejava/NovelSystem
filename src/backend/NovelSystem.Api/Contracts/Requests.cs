namespace NovelSystem.Api.Contracts;
/// <summary>AI 新小说生成请求。</summary>
public sealed record GenerateNovelRequest(string Title,long? StyleId,long? SourceNovelId,string Prompt);
/// <summary>人物更新请求，仅暴露允许修改字段。</summary>
public sealed record UpdateCharacterRequest(string Name,string? Gender,string? Personality,string? Description,string? VoiceFile);