using System.Net.Http.Json;
using System.Text.Json;
using NovelSystem.Application.Contracts;
using NovelSystem.Infrastructure.Persistence;
using NovelSystem.Infrastructure.Services;
namespace NovelSystem.Infrastructure.AI;
/// <summary>llama.cpp OpenAI-compatible Chat Completions 客户端。</summary>
public sealed class LlamaCppChatClient(IHttpClientFactory httpClientFactory,AppDbContext db):IAiChatClient
{
    public async Task<string> ChatAsync(string systemPrompt,string userPrompt,CancellationToken cancellationToken=default)
    {
        var settings=new SettingReader(db);var baseUrl=settings.Get("AiBaseUrl","http://127.0.0.1:8080/v1").TrimEnd('/');var model=settings.Get("AiModel","local-model");
        using var client=httpClientFactory.CreateClient();
        using var response=await client.PostAsJsonAsync($"{baseUrl}/chat/completions",new{model,messages=new[]{new{role="system",content=systemPrompt},new{role="user",content=userPrompt}},temperature=.25,stream=false},cancellationToken);
        var body=await response.Content.ReadAsStringAsync(cancellationToken);response.EnsureSuccessStatusCode();using var json=JsonDocument.Parse(body);return json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()??string.Empty;
    }
}