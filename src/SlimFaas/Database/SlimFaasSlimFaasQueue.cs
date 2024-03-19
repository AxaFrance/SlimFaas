namespace SlimFaas;

public class SlimFaasSlimFaasQueue(IDatabaseService databaseService) : ISlimFaasQueue
{
    private const string KeyPrefix = "Queue:";

    public async Task EnqueueAsync(string key, byte[] data) =>
        await databaseService.ListLeftPushAsync($"{KeyPrefix}{key}", data);

    public async Task<IList<byte[]>> DequeueAsync(string key, long count = 1)
    {
        IList<byte[]> data = await databaseService.ListRightPopAsync($"{KeyPrefix}{key}");
        return data;
    }

    public async Task<long> CountAsync(string key) => await databaseService.ListLengthAsync($"{KeyPrefix}{key}");
}
