using SlimData;

namespace SlimFaas.Database;



public class SlimFaasSlimFaasQueue(IDatabaseService databaseService) : ISlimFaasQueue
{
    private const string KeyPrefix = "Queue:";

    public async Task EnqueueAsync(string key, byte[] data) =>
        await databaseService.ListLeftPushAsync($"{KeyPrefix}{key}", data);

    public async Task<IDictionary<string, byte[]>> DequeueAsync(string key, long count = 1)
    {
        var data = await databaseService.ListRightPopAsync($"{KeyPrefix}{key}");
        return data;
    }

#pragma warning disable CA2252
    public async Task SetQueueItemStatus(string key, IList<Endpoints.QueueItemStatus> queueItemStatus) => await databaseService.ListSetQueueItemStatus($"{KeyPrefix}{key}", queueItemStatus);
#pragma warning restore CA2252

    public async Task<long> CountAsync(string key) => await databaseService.ListLengthAsync($"{KeyPrefix}{key}");
}
