using Microsoft.EntityFrameworkCore;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Persistence;
namespace NovelSystem.Api.Endpoints;
/// <summary>本地 AI、TTS、音色目录和 FFmpeg 配置 API。</summary>
public static class SettingEndpoints
{
    public static IEndpointRouteBuilder MapSettingEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/settings").WithTags("Settings");
        group.MapGet("/",(AppDbContext db)=>db.Settings.ToDictionary(x=>x.Key,x=>x.Value));
        group.MapPut("/",async(Dictionary<string,string> values,AppDbContext db)=>{
            foreach(var item in values){var entity=await db.Settings.FirstOrDefaultAsync(x=>x.Key==item.Key);if(entity is null)db.Settings.Add(new SystemSetting{Key=item.Key,Value=item.Value});else entity.Value=item.Value;}
            await db.SaveChangesAsync();return Results.Ok();
        });
        group.MapGet("/voices",(AppDbContext db)=>{
            var directory=db.Settings.FirstOrDefault(x=>x.Key=="VoiceDirectory")?.Value??"voices";if(!Directory.Exists(directory))return Results.Ok(Array.Empty<object>());
            return Results.Ok(Directory.EnumerateFiles(directory,"*.wav").Select(path=>new{name=Path.GetFileNameWithoutExtension(path),path}));
        });
        return app;
    }
}