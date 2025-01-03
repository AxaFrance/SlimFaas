﻿using SlimData;

namespace SlimFaas;

public interface IDatabaseService
{
    Task<byte[]?> GetAsync(string key);
    Task SetAsync(string key,  byte[] value);
    Task HashSetAsync(string key, IDictionary<string, string> values);
    Task<IDictionary<string, string>> HashGetAllAsync(string key);
    Task ListLeftPushAsync(string key, byte[] field, RetryInformation retryInformation);
    Task<IList<QueueData>?> ListRightPopAsync(string key, int count = 1);
    Task<long> ListCountAvailableElementAsync(string key, int maximum = int.MaxValue);
    Task<long> ListCountElementAsync(string key, int maximum = int.MaxValue);
    Task ListCallbackAsync(string key, ListQueueItemStatus queueItemStatus);
}
