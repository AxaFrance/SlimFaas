namespace SlimFaas;


public class RedisQueue : IQueue
{
    private readonly RedisService _redisService;
    private const string KeyPrefix = "Queue:";
    public RedisQueue(RedisService redisService)
    {
        _redisService = redisService;
    }

    public void EnqueueAsync(string key, string data)
    {
       _redisService.ListLeftPush($"{KeyPrefix}{key}", data);
    }
        
    public string? DequeueAsync(string key)
    {
        var data = _redisService.ListRightPop($"{KeyPrefix}{key}");
        return data;
    }

    public long Count(string key)
    {
        return _redisService.ListLength($"{KeyPrefix}{key}");
    }

}