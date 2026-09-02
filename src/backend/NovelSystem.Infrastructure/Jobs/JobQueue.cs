using System.Collections.Concurrent;

namespace NovelSystem.Infrastructure.Jobs;

/// <summary>
/// 单消费者 FIFO 进程内任务队列。任务状态、入队时间和参数均持久化到数据库。
/// 同一个 JobId 只允许在内存队列中存在一次，避免重复点击/恢复造成重复执行。
/// </summary>
public sealed class JobQueue
{
    private readonly ConcurrentQueue<JobMessage> _queue = new();
    private readonly ConcurrentDictionary<long, byte> _queuedIds = new();
    private readonly SemaphoreSlim _signal = new(0);

    public bool Enqueue(JobMessage message)
    {
        if (!_queuedIds.TryAdd(message.JobId, 0))
            return false;

        _queue.Enqueue(message);
        _signal.Release();
        return true;
    }

    public async Task<JobMessage> DequeueAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await _signal.WaitAsync(cancellationToken);
            if (!_queue.TryDequeue(out var message))
                continue;

            _queuedIds.TryRemove(message.JobId, out _);
            return message;
        }
    }
}

public sealed record JobMessage(long JobId, string Type, string Payload);