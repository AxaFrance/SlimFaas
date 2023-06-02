namespace SlimFaas;

public class HistoryHttpMemoryService
{
    private readonly IDictionary<string, long> _local = new Dictionary<string, long>();

    
    public long GetTicksLastCall(string functionName)
    {
        var result = 0L;
        lock (this)
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
       lock (this)
       {
           _local[functionName] = ticks;    
       }
    }
    
}