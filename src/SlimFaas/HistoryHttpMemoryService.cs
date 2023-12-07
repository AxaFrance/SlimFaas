namespace SlimFaas;

public class HistoryHttpMemoryService
{
    private readonly IDictionary<string, long> _local = new Dictionary<string, long>();
    private readonly ReaderWriterLockSlim _readerWriterLockSlim = new();

    public long GetTicksLastCall(string functionName)
    {
        _readerWriterLockSlim.EnterReadLock();
        try
        {
            return _local.TryGetValue(functionName, out long value) ? value : 0L;
        }
        finally
        {
            _readerWriterLockSlim.ExitReadLock();
        }
    }

    public void SetTickLastCall(string functionName, long ticks)
    {
        _readerWriterLockSlim.EnterWriteLock();
        try
        {
            _local[functionName] = ticks;
        }
        finally
        {
            _readerWriterLockSlim.ExitWriteLock();
        }
    }

    ~HistoryHttpMemoryService() => _readerWriterLockSlim?.Dispose();
}
