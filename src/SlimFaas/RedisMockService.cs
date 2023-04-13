using System.Collections.Concurrent;

namespace SlimFaas;

public class RedisMockService : IRedisService
{

    private ConcurrentDictionary<string, string> keys = new();
    private ConcurrentDictionary<string, List<string>> queue = new();
    private ConcurrentDictionary<string, IDictionary<string, string>> hashSet = new();
    public Task<string> GetAsync(string key)
    {
        if (keys.ContainsKey(key))
        {
            return Task.FromResult(keys[key]);
        }
        return Task.FromResult<string>("");
    }

    public Task SetAsync(string key, string value)
    {
        if (keys.ContainsKey(key))
        {
            keys[key] = value;
        }
        else
        {
            keys.TryAdd(key, value);
        }
        return Task.CompletedTask;
    }

    public Task HashSetAsync(string key, IDictionary<string, string> values)
    {
        if (hashSet.ContainsKey(key))
        {
            hashSet[key] = values;
        }
        else
        {
            hashSet.TryAdd(key, values);
        }
        return Task.CompletedTask;
    }

    public Task<IDictionary<string, string>> HashGetAllAsync(string key)
    {
        if (hashSet.ContainsKey(key))
        {
            return Task.FromResult(hashSet[key]);
        }
        return Task.FromResult<IDictionary<string, string>>(new Dictionary<string, string>());
    }

    public Task ListLeftPushAsync(string key, string field)
    {
        List<string> list;
        if (queue.ContainsKey(key))
        {
            list = queue[key];
        }
        else
        {
            list = new List<string>();
            queue.TryAdd(key, list);
        }
        list.Add(field);
        return Task.CompletedTask;
    }

    public Task<IList<string>> ListRightPopAsync(string key, long count = 1)
    {
        if (queue.ContainsKey(key))
        {
            var list = queue[key];
            
            var listToReturn = list.TakeLast((int)count).ToList();
            if (listToReturn.Count > 0)
            {
                list.RemoveRange(listToReturn.Count - 1, listToReturn.Count);
                return Task.FromResult<IList<string>>(listToReturn);
            }
        }
        return Task.FromResult<IList<string>>(new List<string>());
    }

    public Task<long> ListLengthAsync(string key)
    {
        if (queue.ContainsKey(key))
        {
            var list = queue[key];
            
            return Task.FromResult<long>(list.Count);
        }

        return Task.FromResult<long>(0);
    }
}