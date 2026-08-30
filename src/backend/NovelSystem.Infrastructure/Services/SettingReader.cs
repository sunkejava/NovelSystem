using NovelSystem.Infrastructure.Persistence;
namespace NovelSystem.Infrastructure.Services;
/// <summary>集中读取数据库中的动态配置。</summary>
internal sealed class SettingReader(AppDbContext db){public string Get(string key,string fallback)=>db.Settings.FirstOrDefault(x=>x.Key==key)?.Value??fallback;}