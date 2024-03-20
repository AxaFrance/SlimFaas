using MemoryPack;

namespace SlimFaas;

public class HistoryHttpDatabaseService(IDatabaseService databaseService)
{
    public async Task<long> GetTicksLastCallAsync(string functionName)
    {
        byte[]? result = await databaseService.GetAsync(functionName);
        return result != null ? MemoryPackSerializer.Deserialize<long>(result) : 0;
    }

    public async Task SetTickLastCallAsync(string functionName, long ticks) =>
        await databaseService.SetAsync(functionName, MemoryPackSerializer.Serialize(ticks));
}
