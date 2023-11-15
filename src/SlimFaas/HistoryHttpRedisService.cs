namespace SlimFaas;

public class HistoryHttpRedisService
{
    private readonly IDatabaseService _databaseService;

    public HistoryHttpRedisService(IDatabaseService databaseService)
    {
        _databaseService = databaseService;
    }
    
    public async Task<long> GetTicksLastCallAsync(string functionName)
    {
        var result = await _databaseService.GetAsync(functionName);
        return string.IsNullOrEmpty(result) ? 0 : long.Parse(result);
    }
    
    public async Task SetTickLastCallAsync(string functionName, long ticks)
    {
       await _databaseService.SetAsync(functionName, ticks.ToString());
    }
    
}