using System.Collections.Concurrent;
namespace NovelSystem.Infrastructure.Jobs;
/// <summary>进程内任务队列；任务状态和参数均持久化到数据库。</summary>
public sealed class JobQueue
{
    private readonly ConcurrentQueue<JobMessage> _queue=new();private readonly SemaphoreSlim _signal=new(0);
    public void Enqueue(JobMessage message){_queue.Enqueue(message);_signal.Release();}
    public async Task<JobMessage> DequeueAsync(CancellationToken cancellationToken){await _signal.WaitAsync(cancellationToken);return _queue.TryDequeue(out var message)?message:throw new InvalidOperationException("任务队列状态异常。");}
}
public sealed record JobMessage(long JobId,string Type,string Payload);