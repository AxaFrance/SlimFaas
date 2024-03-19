namespace SlimFaas;

public interface IDatabaseService
{
    Task<string> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task HashSetAsync(string key, IDictionary<string, string> values);
    Task<IDictionary<string, string>> HashGetAllAsync(string key);
    Task ListLeftPushAsync(string key, byte[] field);
    Task<IList<byte[]>> ListRightPopAsync(string key, int count = 1);
    Task<long> ListLengthAsync(string key);
}
