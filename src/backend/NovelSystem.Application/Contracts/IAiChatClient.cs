namespace NovelSystem.Application.Contracts;
/// <summary>大语言模型调用抽象，默认由 llama.cpp 实现。</summary>
public interface IAiChatClient { Task<string> ChatAsync(string systemPrompt,string userPrompt,CancellationToken cancellationToken=default); }