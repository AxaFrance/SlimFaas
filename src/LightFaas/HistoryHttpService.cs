namespace LightFaas;

public class HistoryHttpService
{
    private readonly RedisService _redisService;

    public HistoryHttpService(RedisService redisService)
    {
        _redisService = redisService;
    }
    
    public long GetTicksLastCall(string functionName)
    {
        var result = _redisService.Get(functionName);
        return string.IsNullOrEmpty(result) ? 0 : long.Parse(result);
    }
    
    public void SetTickLastCall(string functionName, long ticks)
    {
       _redisService.Set(functionName, ticks.ToString());
    }
    
}