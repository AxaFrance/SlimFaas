namespace LightFaas;


public class Queue : IQueue
{
    private readonly RedisService _redisService;

    public Queue(RedisService redisService)
    {
        _redisService = redisService;
    }

    public void EnqueueAsync(string key, string data)
    {
       _redisService.ListLeftPush(key, data);
    }
        
    public string? DequeueAsync(string key)
    {
        return _redisService.ListRightPop(key);
    }

}