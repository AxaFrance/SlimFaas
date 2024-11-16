using SlimData;

namespace SlimFaas.Database;

public interface ISlimFaasQueue
{
    Task EnqueueAsync(string key, byte[] message);
    Task<IList<QueueData>?> DequeueAsync(string key, long count = 1);
    Task ListSetQueueItemStatusAsync(string key, ListQueueItemStatus queueItemStatus);
    public Task<long> CountAsync(string key);
}
