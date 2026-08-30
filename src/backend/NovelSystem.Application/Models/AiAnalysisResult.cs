namespace NovelSystem.Application.Models;
/// <summary>AI 小说片段解析后的标准结果。</summary>
public sealed class AiAnalysisResult { public List<AiCharacter> Characters { get; set; }=[]; public List<AiScriptLine> Scripts { get; set; }=[]; }
public sealed class AiCharacter { public string Name { get; set; }=string.Empty; public string? Gender { get; set; } public string? Personality { get; set; } public string? Description { get; set; } }
public sealed class AiScriptLine { public string Speaker { get; set; }="旁白"; public string Text { get; set; }=string.Empty; public string? Emotion { get; set; } }