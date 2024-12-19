using SlimData;

namespace SlimFaas.Database;



public class SlimFaasQueue(IDatabaseService databaseService) : ISlimFaasQueue
{
    private const string KeyPrefix = "Queue:";

    public async Task EnqueueAsync(string key, byte[] data) =>
        await databaseService.ListLeftPushAsync($"{KeyPrefix}{key}", data);

    public async Task<IList<QueueData>?> DequeueAsync(string key, long count = 1)
    {
        var data = await databaseService.ListRightPopAsync($"{KeyPrefix}{key}");
        return data;
    }

    public async Task ListSetQueueItemStatusAsync(string key, ListQueueItemStatus queueItemStatus) => await databaseService.ListSetQueueItemStatus($"{KeyPrefix}{key}", queueItemStatus);

    public async Task<long> CountAvailableElementAsync(string key, int maximum = int.MaxValue) => await databaseService.ListCountAvailableElementAsync($"{KeyPrefix}{key}", maximum);

    public async Task<long> CountElementAsync(string key, int maximum = int.MaxValue) => await databaseService.ListCountElementAsync($"{KeyPrefix}{key}", maximum);


}
