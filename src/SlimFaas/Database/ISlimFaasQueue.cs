namespace SlimFaas;

public interface ISlimFaasQueue
{
    Task EnqueueAsync(string key, string message);
    Task<IList<string>> DequeueAsync(string key, long count = 1);

    Task EnqueueAsync(string key, byte[] message);
    Task<IList<byte[]>> DequeueBinAsync(string key, long count = 1);

    public Task<long> CountAsync(string key);
}
