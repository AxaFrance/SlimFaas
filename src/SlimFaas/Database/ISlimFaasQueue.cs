namespace SlimFaas.Database;

public interface ISlimFaasQueue
{
    Task EnqueueAsync(string key, byte[] message);
    Task<IDictionary<string, byte[]>> DequeueAsync(string key, long count = 1);

    public Task<long> CountAsync(string key);
}
