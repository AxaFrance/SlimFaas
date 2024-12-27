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
    private readonly IList<float> _retryInterval = new List<float> { 1, 1, 1 };
    private readonly TimeSpan _timeMaxToWaitForLeader = TimeSpan.FromMilliseconds(3000);

    private ISupplier<SlimDataPayload> SimplePersistentState =>
        serviceProvider.GetRequiredService<ISupplier<SlimDataPayload>>();

    public async Task<byte[]?> GetAsync(string key)
    {
        return await Retry.DoAsync(() => DoGetAsync(key), logger,  _retryInterval);
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
        await Retry.DoAsync(() => DoSetAsync(key, value), logger, _retryInterval);
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
        await Retry.DoAsync(() => DoHashSetAsync(key, values), logger, _retryInterval);
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
        return await Retry.DoAsync(() =>DoHashGetAllAsync(key), logger, _retryInterval);
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

    public async Task ListLeftPushAsync(string key, byte[] field, RetryInformation retryInformation)
    {
        await Retry.DoAsync(() =>DoListLeftPushAsync(key, field, retryInformation), logger, _retryInterval);
    }

    private async Task DoListLeftPushAsync(string key, byte[] field, RetryInformation retryInformation)
    {
        EndPoint endpoint = await GetAndWaitForLeader();
        ListLeftPushInput listLeftPushInput = new(field, MemoryPackSerializer.Serialize(retryInformation));
        byte[] serialize = MemoryPackSerializer.Serialize(listLeftPushInput);
        if (!cluster.LeadershipToken.IsCancellationRequested)
        {
            var simplePersistentState = serviceProvider.GetRequiredService<SlimPersistentState>();
            await Endpoints.ListLeftPushCommand(simplePersistentState, key, serialize, cluster, new CancellationTokenSource());
        }
        else
        {
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri($"{endpoint}SlimData/ListLeftPush?key={key}"));
            request.Content = new ByteArrayContent(serialize);
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
        return await Retry.DoAsync(() => DoListRightPopAsync(key, count), logger, _retryInterval);
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
            if(result != null && result.Items != null)
            {
                foreach (var listItem in result.Items)
                {
                    Console.WriteLine($"DoListRightPopAsync: Id: {listItem.Id}");
                }
            }

            return result?.Items ?? new List<QueueData>();
        }
    }

    public Task<long> ListCountElementAsync(string key, int maximum = Int32.MaxValue)
    {
        return Retry.DoAsync(() => DoListCountElementAsync(key, maximum), logger, _retryInterval);
    }

    private async Task<long> DoListCountElementAsync(string key, int maximum)
    {
        await GetAndWaitForLeader();
        await MasterWaitForleaseToken();

        SlimDataPayload data = SimplePersistentState.Invoke();

        if (data.Queues.TryGetValue(key, out List<QueueElement>? value))
        {
            var nowTicks = DateTime.UtcNow.Ticks;
            var elements = value.GetQueueAvailableElement(nowTicks, maximum);
            var runningElements = value.GetQueueRunningElement(nowTicks);
            var runningWaitingForRetryElements = value.GetQueueWaitingForRetryElement(nowTicks);
            return elements.Count + runningElements.Count + runningWaitingForRetryElements.Count;
        }

        return 0L;
    }

    public async Task ListCallbackAsync(string key, ListQueueItemStatus queueItemStatus)
    {
        await Retry.DoAsync(() => DoListCallbackAsync(key, queueItemStatus), logger, _retryInterval);
    }

    private async Task DoListCallbackAsync(string key, ListQueueItemStatus queueItemStatus)
    {
        EndPoint endpoint = await GetAndWaitForLeader();
        if (!cluster.LeadershipToken.IsCancellationRequested)
        {
            var simplePersistentState = serviceProvider.GetRequiredService<SlimPersistentState>();
            await Endpoints.ListCallbackCommandAsync(simplePersistentState, key, queueItemStatus, cluster, new CancellationTokenSource());
        }
        else
        {
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri($"{endpoint}SlimData/ListCallback?key={key}"));
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

    public async Task<long> ListCountAvailableElementAsync(string key, int maximum)
    {
        return await Retry.DoAsync(() => DoListCountAvailableElementAsync(key, maximum), logger, _retryInterval);
    }

    private async Task<long> DoListCountAvailableElementAsync(string key, int maximum)
    {
        await GetAndWaitForLeader();
        await MasterWaitForleaseToken();

        SlimDataPayload data = SimplePersistentState.Invoke();

        if (data.Queues.TryGetValue(key, out List<QueueElement>? value))
        {
            var elements = value.GetQueueAvailableElement(DateTime.UtcNow.Ticks, maximum);
            var number = elements.Count;
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

    public static async Task<T> DoAsync<T>(
            Func<Task<T>> action,
            ILogger logger,
            IList<float> delays
            )
        {
            var exceptions = new List<Exception>();

            for (int attempt = 0; attempt < delays.Count; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        var delay = delays[attempt];
                        logger.LogWarning("Try {Attempt} : wait numnber {Delay} second", attempt, delay);
                        await Task.Delay((int)delay * 1000);
                    }

                    return await action();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }

            throw new AggregateException(exceptions);
        }

    public static async Task DoAsync(
        Func<Task> action,
        ILogger logger,
        IList<float> delays
)
    {
        var exceptions = new List<Exception>();

        for (int attempt = 0; attempt < delays.Count; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var delay = delays[attempt];
                    logger.LogWarning("Try {Attempt} : wait numnber {Delay} second", attempt, delay);
                    await Task.Delay((int)delay * 1000);
                }

                // Exécuter la méthode asynchrone
                await action();
                return; // Si succès, on sort de la fonction
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        // Si toutes les tentatives échouent, lever une AggregateException
        throw new AggregateException(exceptions);
    }
}
