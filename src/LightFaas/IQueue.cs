namespace WebApplication1;

public interface IQueue
{
    public IList<QueueKey> Keys { get; }
    void EnqueueAsync(string key, string message);
    string DequeueAsync(string type);
}