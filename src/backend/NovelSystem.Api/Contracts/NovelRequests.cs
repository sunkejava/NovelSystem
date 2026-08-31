namespace NovelSystem.Api.Contracts;

/// <summary>编辑小说资产请求。</summary>
public sealed record UpdateNovelRequest(string Title, string Content);

/// <summary>设置小说旁白默认音色。</summary>
public sealed record UpdateNarratorVoiceRequest(long? VoiceProfileId);
