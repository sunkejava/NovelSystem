namespace NovelSystem.Api.Contracts;

/// <summary>AI 新小说生成请求。</summary>
public sealed record GenerateNovelRequest(string Title, long? StyleId, long? SourceNovelId, string Prompt);

/// <summary>人物更新请求，仅暴露允许修改字段。</summary>
public sealed record UpdateCharacterRequest(
    string Name,
    string? Gender,
    string? Personality,
    string? Description,
    long? VoiceProfileId,
    string? VoiceFile);

/// <summary>创建或修改音色配置。</summary>
public sealed record SaveVoiceProfileRequest(
    string Name,
    string ReferenceAudioFile,
    string ReferenceText,
    bool UseXVector,
    string Language);

/// <summary>
/// 根据本地音色目录批量创建音色档案。
/// 音色名称自动使用 WAV 文件名，其他字段使用本请求中的统一默认值。
/// </summary>
public sealed record BatchVoiceProfileRequest(
    string ReferenceText,
    bool UseXVector,
    string Language,
    bool SkipExisting = true,
    bool BuildPrompt = false);
