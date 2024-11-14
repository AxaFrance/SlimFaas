﻿using System.Collections.Concurrent;
using SlimData;

namespace SlimFaas;

public class DatabaseMockService : IDatabaseService
{
    private readonly ConcurrentDictionary<string, IDictionary<string, string>> hashSet = new();

    private readonly ConcurrentDictionary<string, byte[]> keys = new();
    private readonly ConcurrentDictionary<string, List<QueueData>> queue = new();

    public Task<byte[]?> GetAsync(string key)
    {
        if (keys.TryGetValue(key, out byte[]? value))
        {
            return Task.FromResult(value)!;
        }

        return Task.FromResult<byte[]?>(null);
    }

    public Task SetAsync(string key, byte[] value)
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

    public Task ListLeftPushAsync(string key, byte[] field)
    {
        List<QueueData> list;
        if (queue.ContainsKey(key))
        {
            list = queue[key];
        }
        else
        {
            list = new List<QueueData>();
            queue.TryAdd(key, list);
        }

        list.Add(new QueueData(Guid.NewGuid().ToString(), field));
        return Task.CompletedTask;
    }

    public Task<IList<QueueData>?> ListRightPopAsync(string key, int count = 1)
    {
        if (!queue.ContainsKey(key))
        {
            return Task.FromResult<IList<QueueData>?>(new List<QueueData>());
        }

        var list = queue[key];
        var listToReturn = list.TakeLast(count).ToList();
        if (listToReturn.Count > 0)
        {
            list.RemoveRange(listToReturn.Count - 1, listToReturn.Count);
            return Task.FromResult<IList<QueueData>?>(listToReturn);
        }

        return Task.FromResult<IList<QueueData>?>(new List<QueueData>());
    }

    public Task<long> ListLengthAsync(string key)
    {
        if (!queue.ContainsKey(key))
        {
            return Task.FromResult<long>(0);
        }

        var list = queue[key];

        return Task.FromResult<long>(list.Count);
    }

    public async Task ListSetQueueItemStatus(string key, List<QueueItemStatus> queueItemStatus)
    {
        await Task.Delay(100);
    }
}
