namespace LightFaas;

public interface IQueue
{
    void EnqueueAsync(string key, string message);
    string DequeueAsync(string type);
}