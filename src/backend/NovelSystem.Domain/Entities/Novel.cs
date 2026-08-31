using NovelSystem.Domain.Common;
namespace NovelSystem.Domain.Entities;
/// <summary>小说主实体，保存上传或 AI 生成的完整正文。</summary>
public sealed class Novel : Entity { public string Title { get; set; }=string.Empty; public string SourceFile { get; set; }=string.Empty; public string Content { get; set; }=string.Empty; public string Status { get; set; }=NovelStatus.Uploaded; public long? NarratorVoiceProfileId { get; set; } public DateTime CreatedAt { get; set; }=DateTime.UtcNow; }
public static class NovelStatus { public const string Uploaded="Uploaded"; public const string Analyzing="Analyzing"; public const string Analyzed="Analyzed"; }