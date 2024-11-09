﻿using SlimData;

namespace SlimFaas;

public interface IDatabaseService
{
    Task<byte[]?> GetAsync(string key);
    Task SetAsync(string key,  byte[] value);
    Task HashSetAsync(string key, IDictionary<string, string> values);
    Task<IDictionary<string, string>> HashGetAllAsync(string key);
    Task ListLeftPushAsync(string key, byte[] field);
    Task<IDictionary<string, byte[]>> ListRightPopAsync(string key, int count = 1);
    Task<long> ListLengthAsync(string key);
    Task ListSetQueueItemStatus(string key, IList<Endpoints.QueueItemStatus> queueItemStatus);
}
