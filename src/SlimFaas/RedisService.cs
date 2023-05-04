using StackExchange.Redis;

namespace SlimFaas;

public class RedisService
{
    private IDatabase _database;
    private const string KeyPrefix = "SlimFaas:";

    public RedisService()
    {
        var redisConnectionString = 
        Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING") ?? "localhost:6379";
        var redis = ConnectionMultiplexer.Connect(redisConnectionString);
        _database = redis.GetDatabase();
    }
    
    public string Get(string key)
    {
        return _database.StringGet(KeyPrefix+key).ToString();
    }
    
    public void Set(string key, string value)
    {
         _database.StringGetSet(KeyPrefix+key, value);
    }

    public void HashSet(string key, IDictionary<string, string> values)
    {
        _database.HashSet(KeyPrefix+key, values.Select(x => new HashEntry(x.Key, x.Value)).ToArray());
    }
    
    public IDictionary<string, string> HashGetAll(string key)
    {
        return _database.HashGetAll(KeyPrefix+key).ToStringDictionary();
    }
    
    public void ListLeftPush(string key, string field)
    {
        _database.ListLeftPush(KeyPrefix+key, field);
    }

    public IList<string> ListRightPop(string key, long count = 1)
    {
        IList<string> resultList = new List<string>();
        var results = _database.ListRightPop(KeyPrefix+key, count);
        foreach (var redisValue in results)
        {
            if (redisValue.HasValue)
            {
                resultList.Add(redisValue.ToString());
            }
        }
        return resultList;
    }
    
    public long ListLength(string key)
    {
        return _database.ListLength(KeyPrefix+key);
    }
}