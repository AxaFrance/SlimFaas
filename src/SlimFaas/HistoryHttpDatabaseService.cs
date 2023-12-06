namespace SlimFaas;

public class HistoryHttpDatabaseService(IDatabaseService databaseService)
{
    public async Task<long> GetTicksLastCallAsync(string functionName)
    {
        var result = await databaseService.GetAsync(functionName);
        return string.IsNullOrEmpty(result) ? 0 : long.Parse(result);
    }

    public async Task SetTickLastCallAsync(string functionName, long ticks)
    {
       await databaseService.SetAsync(functionName, ticks.ToString());
    }

}
