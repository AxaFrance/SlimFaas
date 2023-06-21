namespace SlimFaas;

public class HistoryHttpMemoryService
{
    private readonly IDictionary<string, long> _local = new Dictionary<string, long>();
    private readonly object Lock = new();
    
    public long GetTicksLastCall(string functionName)
    {
        var result = 0L;
        lock (Lock)
        {
            if (_local.TryGetValue(functionName, out var value))
            {
                result = value;
            }
        }

        return result;
    }
    
    public void SetTickLastCall(string functionName, long ticks)
    {
       lock (Lock)
       {
           _local[functionName] = ticks;    
       }
    }
    
}