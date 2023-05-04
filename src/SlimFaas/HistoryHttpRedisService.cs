namespace SlimFaas;

public class HistoryHttpRedisService
{
    private readonly RedisService _redisService;

    public HistoryHttpRedisService(RedisService redisService)
    {
        _redisService = redisService;
    }
    
    public async Task<long> GetTicksLastCallAsync(string functionName)
    {
        var result = await _redisService.GetAsync(functionName);
        return string.IsNullOrEmpty(result) ? 0 : long.Parse(result);
    }
    
    public async Task SetTickLastCallAsync(string functionName, long ticks)
    {
       await _redisService.SetAsync(functionName, ticks.ToString());
    }
    
}