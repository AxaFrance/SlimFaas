using SlimData;

namespace SlimFaas.Database;

public interface ISlimFaasQueue
{
    Task EnqueueAsync(string key, byte[] message);
    Task<IList<QueueData>?> DequeueAsync(string key, long count = 1);
    Task ListCallbackAsync(string key, ListQueueItemStatus queueItemStatus);
    public Task<long> CountAvailableElementAsync(string key, int maximum = int.MaxValue);
    public Task<long> CountElementAsync(string key, int maximum = int.MaxValue);
}
