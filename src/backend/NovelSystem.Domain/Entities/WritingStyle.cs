using NovelSystem.Domain.Common;
namespace NovelSystem.Domain.Entities;
/// <summary>从小说中学习并沉淀的写作风格。</summary>
public sealed class WritingStyle : Entity { public long? NovelId { get; set; } public string Name { get; set; }=string.Empty; public string Summary { get; set; }=string.Empty; public string PromptTemplate { get; set; }=string.Empty; }