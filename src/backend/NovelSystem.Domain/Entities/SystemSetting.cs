using NovelSystem.Domain.Common;
namespace NovelSystem.Domain.Entities;
/// <summary>系统动态配置。</summary>
public sealed class SystemSetting : Entity { public string Key { get; set; }=string.Empty; public string Value { get; set; }=string.Empty; }