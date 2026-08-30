using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Jobs;
using NovelSystem.Infrastructure.Persistence;
namespace NovelSystem.Api.Endpoints;
/// <summary>小说上传、详情、解析和音频任务 API。</summary>
public static class NovelEndpoints
{
    public static IEndpointRouteBuilder MapNovelEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/novels").WithTags("Novels");
        group.MapGet("/",async(AppDbContext db)=>await db.Novels.OrderByDescending(x=>x.Id).ToListAsync());
        group.MapPost("/upload",async(HttpRequest request,AppDbContext db)=>{
            var form=await request.ReadFormAsync();var file=form.Files["file"]??throw new BadHttpRequestException("必须上传小说文件。");
            using var reader=new StreamReader(file.OpenReadStream());var novel=new Novel{Title=form["title"].FirstOrDefault()??Path.GetFileNameWithoutExtension(file.FileName),SourceFile=file.FileName,Content=await reader.ReadToEndAsync()};
            db.Novels.Add(novel);await db.SaveChangesAsync();return Results.Ok(novel);
        });
        group.MapGet("/{id:long}",async(long id,AppDbContext db)=>{
            var novel=await db.Novels.FindAsync(id);if(novel is null)return Results.NotFound();
            return Results.Ok(new{novel,characters=await db.Characters.Where(x=>x.NovelId==id).OrderBy(x=>x.Id).ToListAsync(),scripts=await db.ScriptLines.Where(x=>x.NovelId==id).OrderBy(x=>x.Order).ToListAsync()});
        });
        group.MapPost("/{id:long}/analyze",(long id,AppDbContext db,JobQueue queue)=>EnqueueAsync(db,queue,"AnalyzeNovel",id));
        group.MapPost("/{id:long}/audio",(long id,AppDbContext db,JobQueue queue)=>EnqueueAsync(db,queue,"GenerateAudio",id));
        return app;
    }
    private static async Task<IResult> EnqueueAsync(AppDbContext db,JobQueue queue,string type,long novelId)
    {
        var payload=JsonSerializer.Serialize(new{novelId});var job=new JobRecord{Type=type,Payload=payload};db.Jobs.Add(job);await db.SaveChangesAsync();queue.Enqueue(new JobMessage(job.Id,type,payload));return Results.Ok(job);
    }
}