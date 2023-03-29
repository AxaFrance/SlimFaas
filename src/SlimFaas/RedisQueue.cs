namespace SlimFaas;


public class RedisQueue : IQueue
{
    private readonly RedisService _redisService;

    public RedisQueue(RedisService redisService)
    {
        _redisService = redisService;
    }

    public void EnqueueAsync(string key, string data)
    {
       _redisService.ListLeftPush($"faaslight_{key}", data);
    }
        
    public string? DequeueAsync(string key)
    {
        var data = _redisService.ListRightPop($"faaslight_{key}");
        return data;
    }

}