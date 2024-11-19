using System.Data;
using System.Net;
using DotNext;
using DotNext.Net.Cluster.Consensus.Raft;
using MemoryPack;
using SlimData;
using SlimData.Commands;

namespace SlimFaas.Database;
#pragma warning disable CA2252
public class SlimDataService(IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider, IRaftCluster cluster, ILogger<SlimDataService> logger)
    : IDatabaseService
{
    public const string HttpClientName = "SlimDataHttpClient";
    private const int MaxAttemptCount = 3;
    private readonly TimeSpan _retryInterval = TimeSpan.FromSeconds(1);
    private readonly TimeSpan _timeMaxToWaitForLeader = TimeSpan.FromMilliseconds(3000);

    private ISupplier<SlimDataPayload> SimplePersistentState =>
        serviceProvider.GetRequiredService<ISupplier<SlimDataPayload>>();

    public async Task<byte[]?> GetAsync(string key)
    {
        return await Retry.Do(() => DoGetAsync(key), _retryInterval, logger, MaxAttemptCount);
    }

    private async Task<byte[]?> DoGetAsync(string key)
    {
        await GetAndWaitForLeader();
        await MasterWaitForleaseToken();
        SlimDataPayload data = SimplePersistentState.Invoke();
        return data.KeyValues.TryGetValue(key, out ReadOnlyMemory<byte> value) ? value.ToArray() : null;
    }

    public async Task SetAsync(string key, byte[] value)
    {
        await Retry.Do(() =>DoSetAsync(key, value), _retryInterval, logger, MaxAttemptCount);
    }

    private async Task DoSetAsync(string key,  byte[] value)
    {
        EndPoint endpoint = await GetAndWaitForLeader();

        if (!cluster.LeadershipToken.IsCancellationRequested)
        {
            var simplePersistentState = serviceProvider.GetRequiredService<SlimPersistentState>();
            await Endpoints.AddKeyValueCommand(simplePersistentState, key, value, cluster, new CancellationTokenSource());
        }
        else
        {
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri($"{endpoint}SlimData/AddKeyValue?key={key}"));
            request.Content = new ByteArrayContent(value);
            using var httpClient = httpClientFactory.CreateClient(HttpClientName);
            using HttpResponseMessage response = await httpClient.SendAsync(request);
            if ((int)response.StatusCode >= 500)
            {
                throw new DataException("Error in calling SlimData HTTP Service");
            }
        }
    }

    public async Task HashSetAsync(string key, IDictionary<string, string> values)
    {
        await Retry.Do(() =>DoHashSetAsync(key, values), _retryInterval, logger, MaxAttemptCount);
    }

    private async Task DoHashSetAsync(string key, IDictionary<string, string> values)
    {
        EndPoint endpoint = await GetAndWaitForLeader();
        if (!cluster.LeadershipToken.IsCancellationRequested)
        {
            var simplePersistentState = serviceProvider.GetRequiredService<SlimPersistentState>();
            await Endpoints.AddHashSetCommand(simplePersistentState, key, new Dictionary<string, string>(values), cluster, new CancellationTokenSource());
        }
        else
        {
            MultipartFormDataContent multipart = new();
            multipart.Add(new StringContent(key), "______key_____");
            foreach (KeyValuePair<string, string> value in values)
            {
                multipart.Add(new StringContent(value.Value), value.Key);
            }
            using var httpClient = httpClientFactory.CreateClient(HttpClientName);
            using HttpResponseMessage response =
                await httpClient.PostAsync(new Uri($"{endpoint}SlimData/AddHashset"), multipart);
            if ((int)response.StatusCode >= 500)
            {
                throw new DataException("Error in calling SlimData HTTP Service");
            }
        }
    }

    public async Task<IDictionary<string, string>> HashGetAllAsync(string key)
    {
        return await Retry.Do(() =>DoHashGetAllAsync(key), _retryInterval, logger, MaxAttemptCount);
    }

    private async Task<IDictionary<string, string>> DoHashGetAllAsync(string key)
    {
        await GetAndWaitForLeader();
        await MasterWaitForleaseToken();

        SlimDataPayload data = SimplePersistentState.Invoke();
        return data.Hashsets.TryGetValue(key, out Dictionary<string, string>? value)
            ? (IDictionary<string, string>)value
            : new Dictionary<string, string>();
    }

    public async Task ListLeftPushAsync(string key, byte[] field)
    {
        await Retry.Do(() =>DoListLeftPushAsync(key, field), _retryInterval, logger, MaxAttemptCount);
    }

    private async Task DoListLeftPushAsync(string key, byte[] field)
    {
        EndPoint endpoint = await GetAndWaitForLeader();
        if (!cluster.LeadershipToken.IsCancellationRequested)
        {
            var simplePersistentState = serviceProvider.GetRequiredService<SlimPersistentState>();
            await Endpoints.ListLeftPushCommand(simplePersistentState, key, field, cluster, new CancellationTokenSource());
        }
        else
        {
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri($"{endpoint}SlimData/ListLeftPush?key={key}"));
            request.Content = new ByteArrayContent(field);
            using var httpClient = httpClientFactory.CreateClient(HttpClientName);
            HttpResponseMessage response = await httpClient.SendAsync(request);
            if ((int)response.StatusCode >= 500)
            {
                throw new DataException("Error in calling SlimData HTTP Service");
            }
        }
    }

    public async Task<IList<QueueData>?> ListRightPopAsync(string key, int count = 1)
    {
        return await Retry.Do(() => DoListRightPopAsync(key, count), _retryInterval, logger, MaxAttemptCount);
    }

    private async Task<IList<QueueData>?> DoListRightPopAsync(string key, int count = 1)
    {
        EndPoint endpoint = await GetAndWaitForLeader();
        if (!cluster.LeadershipToken.IsCancellationRequested)
        {
            var simplePersistentState = serviceProvider.GetRequiredService<SlimPersistentState>();
            var result = await Endpoints.ListRightPopCommand(simplePersistentState, key, count, cluster, new CancellationTokenSource());
            return result.Items;
        }
        else
        {
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri($"{endpoint}SlimData/ListRightPop"));
            MultipartFormDataContent multipart = new();
            multipart.Add(new StringContent(count.ToString()), key);

            request.Content = multipart;
            using var httpClient = httpClientFactory.CreateClient(HttpClientName);
            using HttpResponseMessage response = await httpClient.SendAsync(request);
            if ((int)response.StatusCode >= 500)
            {
                throw new DataException("Error in calling SlimData HTTP Service");
            }

            var bin = await response.Content.ReadAsByteArrayAsync();
            ListItems? result = MemoryPackSerializer.Deserialize<ListItems>(bin);
            return result?.Items ?? new List<QueueData>();
        }
    }

    public async Task ListSetQueueItemStatus(string key, ListQueueItemStatus queueItemStatus)
    {
        await Retry.Do(() => DoListSetQueueItemStatus(key, queueItemStatus), _retryInterval, logger, MaxAttemptCount);
    }

    private async Task DoListSetQueueItemStatus(string key, ListQueueItemStatus queueItemStatus)
    {
        EndPoint endpoint = await GetAndWaitForLeader();
        if (!cluster.LeadershipToken.IsCancellationRequested)
        {
            var simplePersistentState = serviceProvider.GetRequiredService<SlimPersistentState>();
            await Endpoints.ListSetQueueItemStatusCommand(simplePersistentState, key, queueItemStatus, cluster, new CancellationTokenSource());
        }
        else
        {
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri($"{endpoint}SlimData/ListSetQueueItemStatus?key={key}"));
            var field = MemoryPackSerializer.Serialize(queueItemStatus);
            request.Content = new ByteArrayContent(field);
            using var httpClient = httpClientFactory.CreateClient(HttpClientName);
            using HttpResponseMessage response = await httpClient.SendAsync(request);
            if ((int)response.StatusCode >= 500)
            {
                throw new DataException("Error in calling SlimData HTTP Service");
            }
        }
    }

    public async Task<long> ListLengthAsync(string key)
    {
        return await Retry.Do(() => DoListLengthAsync(key), _retryInterval, logger, MaxAttemptCount);
    }

    private async Task<long> DoListLengthAsync(string key)
    {
        await GetAndWaitForLeader();
        await MasterWaitForleaseToken();

        SlimDataPayload data = SimplePersistentState.Invoke();

        if (data.Queues.TryGetValue(key, out List<QueueElement>? value))
        {
            var elements = value.GetQueueAvailableElement([2, 6, 10], DateTime.UtcNow.Ticks, int.MaxValue);

            foreach (QueueElement queueElement in elements)
            {
                Console.WriteLine($"DoListLengthAsync: Element Id: {queueElement.Id}");
                foreach (QueueHttpTryElement queueElementRetryQueueElement in queueElement.RetryQueueElements)
                {
                    Console.WriteLine($"DoListLengthAsync  StartTimeStamp: {queueElementRetryQueueElement.StartTimeStamp} EndTimeStamp: {queueElementRetryQueueElement.EndTimeStamp} HttpCode: {queueElementRetryQueueElement.HttpCode}  ");
                }
            }

            var number = elements.Count;
            Console.WriteLine($"DoListLengthAsync: Number of available elements: {number} for key: {key}");
            return number;
        }

        return 0L;
    }

    private async Task MasterWaitForleaseToken()
    {
        while (cluster.TryGetLeaseToken(out var leaseToken) && leaseToken.IsCancellationRequested)
        {
            Console.WriteLine("Master node is waiting for lease token");
            await Task.Delay(10);
        }
    }

    private async Task<EndPoint> GetAndWaitForLeader()
    {
        TimeSpan timeWaited = TimeSpan.Zero;
        while (cluster.Leader == null && timeWaited < _timeMaxToWaitForLeader)
        {
            await Task.Delay(500);
            timeWaited += TimeSpan.FromMilliseconds(500);
        }

        if (cluster.Leader == null)
        {
            throw new DataException("No leader found");
        }

        return cluster.Leader.EndPoint;
    }
}
#pragma warning restore CA2252
public static class Retry
{

    public static T Do<T>(
        Func<T> action,
        TimeSpan retryInterval,
        ILogger<SlimDataService> logger,
        int maxAttemptCount = 3)
    {
        var exceptions = new List<Exception>();

        for (int attempted = 0; attempted < maxAttemptCount; attempted++)
        {
            try
            {
                if (attempted > 0)
                {
                    Task.Delay(retryInterval).Wait();
                    logger.LogWarning("SlimDataService Retry number {RetryInterval}", retryInterval);
                }
                return action();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }
        throw new AggregateException(exceptions);
    }
}
