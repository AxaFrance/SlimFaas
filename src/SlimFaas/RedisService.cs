using StackExchange.Redis;

namespace SlimFaas;

public class RedisService
{
    private ConnectionMultiplexer _redis;
    private const string KeyPrefix = "SlimFaas:";

    public RedisService()
    {
        var redisConnectionString = 
        Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING") ?? "localhost:6379";
        _redis = ConnectionMultiplexer.Connect(redisConnectionString);
    }
    
    public async Task<string> GetAsync(string key)
    {
        return (await _redis.GetDatabase().StringGetAsync(KeyPrefix+key)).ToString();
    }
    
    public async Task SetAsync(string key, string value)
    {
        await _redis.GetDatabase().StringGetSetAsync(KeyPrefix+key, value);
    }

    public async Task HashSetAsync(string key, IDictionary<string, string> values)
    {
        await _redis.GetDatabase().HashSetAsync(KeyPrefix+key, values.Select(x => new HashEntry(x.Key, x.Value)).ToArray());
    }
    
    public async Task<IDictionary<string, string>> HashGetAllAsync(string key)
    {
        var hashEntries = await _redis.GetDatabase().HashGetAllAsync(KeyPrefix + key);
        return hashEntries.ToStringDictionary();
    }
    
    public async Task ListLeftPushAsync(string key, string field)
    {
        await _redis.GetDatabase().ListLeftPushAsync(KeyPrefix+key, field);
    }

    public async Task<IList<string>> ListRightPopAsync(string key, long count = 1)
    {
        IList<string> resultList = new List<string>();
        var results = await _redis.GetDatabase().ListRightPopAsync(KeyPrefix+key, count);
        if (results == null)
        {
            return resultList;
        }
        foreach (var redisValue in results)
        {
            if (redisValue.HasValue)
            {
                resultList.Add(redisValue.ToString());
            }
        }
        return resultList;
    }
    
    public async Task<long> ListLengthAsync(string key)
    {
        return await _redis.GetDatabase().ListLengthAsync(KeyPrefix+key);
    }
}