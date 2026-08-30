using Microsoft.EntityFrameworkCore;
using NovelSystem.Infrastructure.Persistence;
namespace NovelSystem.Api.Endpoints;
/// <summary>后台任务查询和结果下载 API。</summary>
public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/jobs").WithTags("Jobs");
        group.MapGet("/",async(AppDbContext db)=>await db.Jobs.OrderByDescending(x=>x.Id).Take(200).ToListAsync());
        group.MapGet("/{id:long}/download",async(long id,AppDbContext db)=>{var job=await db.Jobs.FindAsync(id);return job?.Result is null||!File.Exists(job.Result)?Results.NotFound():Results.File(job.Result,"audio/mpeg",Path.GetFileName(job.Result));});
        return app;
    }
}