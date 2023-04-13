
namespace SlimFaas;


public class RedisQueue : IQueue
{
    private readonly IRedisService _redisService;
    private const string KeyPrefix = "Queue:";
    public RedisQueue(IRedisService redisService)
    {
        _redisService = redisService;
    }

    public async Task EnqueueAsync(string key, string data)
    {
        await _redisService.ListLeftPushAsync($"{KeyPrefix}{key}",  data);
    }
        
    public async Task<IList<string>> DequeueAsync(string key, long count = 1) 
    {
        var data = await _redisService.ListRightPopAsync($"{KeyPrefix}{key}");
        return data;
    }

    public async Task<long> CountAsync(string key)
    {
        return await _redisService.ListLengthAsync($"{KeyPrefix}{key}");
    }

}