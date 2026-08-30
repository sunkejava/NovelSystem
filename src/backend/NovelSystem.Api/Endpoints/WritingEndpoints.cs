using System.Text;
using Microsoft.EntityFrameworkCore;
using NovelSystem.Api.Contracts;
using NovelSystem.Application.Contracts;
using NovelSystem.Domain.Entities;
using NovelSystem.Infrastructure.Persistence;
namespace NovelSystem.Api.Endpoints;
/// <summary>写作手法学习、AI 创作和生成小说回流 API。</summary>
public static class WritingEndpoints
{
    public static IEndpointRouteBuilder MapWritingEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/writing").WithTags("Writing");
        group.MapPost("/learn/{novelId:long}",async(long novelId,AppDbContext db,IAiChatClient ai)=>{
            var novel=await db.Novels.FindAsync(novelId);if(novel is null)return Results.NotFound();
            var sample=novel.Content[..Math.Min(novel.Content.Length,30000)];
            var summary=await ai.ChatAsync("你是小说写作技法研究专家。","系统分析以下小说的叙事视角、语言风格、章节节奏、人物塑造、对白、悬念和情绪推进，并整理成可复用写作方法与提示词模板。\n\n"+sample);
            var style=new WritingStyle{NovelId=novelId,Name=novel.Title+" · 风格模型",Summary=summary,PromptTemplate=summary};db.WritingStyles.Add(style);await db.SaveChangesAsync();return Results.Ok(style);
        });
        group.MapGet("/styles",async(AppDbContext db)=>await db.WritingStyles.OrderByDescending(x=>x.Id).ToListAsync());
        group.MapPost("/generate",async(GenerateNovelRequest request,AppDbContext db,IAiChatClient ai)=>{
            var style=request.StyleId is null?null:await db.WritingStyles.FindAsync(request.StyleId);
            var content=await ai.ChatAsync("你是专业中文小说作者。学习给定技巧，但必须创作全新的故事、人物和文本。",(style?.PromptTemplate??string.Empty)+"\n\n创作任务："+request.Prompt);
            var novel=new GeneratedNovel{Title=request.Title,StyleId=request.StyleId,SourceNovelId=request.SourceNovelId,Prompt=request.Prompt,Content=content};db.GeneratedNovels.Add(novel);await db.SaveChangesAsync();return Results.Ok(novel);
        });
        group.MapGet("/generated",async(AppDbContext db)=>await db.GeneratedNovels.OrderByDescending(x=>x.Id).ToListAsync());
        group.MapGet("/generated/{id:long}/download",async(long id,AppDbContext db)=>{var novel=await db.GeneratedNovels.FindAsync(id);return novel is null?Results.NotFound():Results.File(Encoding.UTF8.GetBytes(novel.Content),"text/plain",novel.Title+".txt");});
        group.MapPost("/generated/{id:long}/publish",async(long id,AppDbContext db)=>{var generated=await db.GeneratedNovels.FindAsync(id);if(generated is null)return Results.NotFound();var novel=new Novel{Title=generated.Title,SourceFile="AI生成",Content=generated.Content};db.Novels.Add(novel);await db.SaveChangesAsync();return Results.Ok(novel);});
        return app;
    }
}