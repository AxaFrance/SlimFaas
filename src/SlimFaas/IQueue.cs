namespace SlimFaas;

public interface IQueue
{
    void EnqueueAsync(string key, string message);
    IList<string> DequeueAsync(string key, long count = 1);
    public long Count(string key);
}