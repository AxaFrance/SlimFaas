
namespace SlimFaas;


public class SlimFaasSlimFaasQueue(IDatabaseService databaseService) : ISlimFaasQueue
{
    private const string KeyPrefix = "Queue:";

    public async Task EnqueueAsync(string key, string data)
    {
        await databaseService.ListLeftPushAsync($"{KeyPrefix}{key}",  data);
    }

    public async Task<IList<string>> DequeueAsync(string key, long count = 1)
    {
        var data = await databaseService.ListRightPopAsync($"{KeyPrefix}{key}");
        return data;
    }

    public async Task<long> CountAsync(string key)
    {
        return await databaseService.ListLengthAsync($"{KeyPrefix}{key}");
    }

}
