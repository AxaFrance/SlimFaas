namespace SlimFaas;

public interface IQueue
{
    void EnqueueAsync(string key, string message);
    string DequeueAsync(string type);
    public long Count(string key);
}