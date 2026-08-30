using NovelSystem.Domain.Entities;
namespace NovelSystem.Infrastructure.Persistence;
/// <summary>初始化数据库和默认本地 AI/TTS 配置。</summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(AppDbContext db,CancellationToken cancellationToken=default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
        var defaults=new Dictionary<string,string>{{"AiBaseUrl","http://127.0.0.1:8080/v1"},{"AiModel","local-model"},{"TtsBaseUrl","http://127.0.0.1:7860"},{"TtsEndpoint","/api/tts"},{"VoiceDirectory","voices"},{"FfmpegPath","ffmpeg"}};
        foreach(var item in defaults) if(!db.Settings.Any(x=>x.Key==item.Key)) db.Settings.Add(new SystemSetting{Key=item.Key,Value=item.Value});
        await db.SaveChangesAsync(cancellationToken);
    }
}