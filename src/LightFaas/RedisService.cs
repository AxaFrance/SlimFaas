using StackExchange.Redis;

namespace LightFaas;

public class RedisService
{
    private IDatabase _database;

    public RedisService()
    {
        var redisConnectionString = $"localhost:6379";
        var redis = ConnectionMultiplexer.Connect(redisConnectionString);
        _database = redis.GetDatabase();
    }
    
    public string Get(string key)
    {
        return _database.StringGet(key).ToString();
    }
    
    public void Set(string key, string value)
    {
         _database.StringGetSet(key, value);
    }

    public void HashSet(string key, IDictionary<string, string> values)
    {
        _database.HashSet(key, values.Select(x => new HashEntry(x.Key, x.Value)).ToArray());
    }
    
    public IDictionary<string, string> HashGetAll(string key)
    {
        return _database.HashGetAll(key).ToStringDictionary();
    }

}