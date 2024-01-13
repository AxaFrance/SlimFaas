using System.Data;
using System.Net;
using System.Text.Json;
using DotNext;
using DotNext.Net.Cluster.Consensus.Raft;
using RaftNode;
using SlimData;

namespace SlimFaas;
#pragma warning disable CA2252
public class SlimDataService(HttpClient httpClient, IServiceProvider serviceProvider, IRaftCluster cluster)
    : IDatabaseService
{
    private ISupplier<SupplierPayload> SimplePersistentState =>
        serviceProvider.GetRequiredService<ISupplier<SupplierPayload>>();

    public async Task<string> GetAsync(string key)
    {
        return await Retry.Do(() =>DoGetAsync(key), TimeSpan.FromSeconds(1), 5);
    }

    private async Task<string> DoGetAsync(string key)
    {
        await GetAndWaitForLeader();
        if (cluster.LeadershipToken.IsCancellationRequested)
        {
            if (cluster.Lease is null or { IsExpired: true })
            {
                await cluster.ApplyReadBarrierAsync();
            }
        }
        SupplierPayload data = SimplePersistentState.Invoke();
        return data.KeyValues.TryGetValue(key, out string? value) ? value : string.Empty;
    }

    public async Task SetAsync(string key, string value)
    {
        await Retry.Do(() =>DoSetAsync(key, value), TimeSpan.FromSeconds(1), 5);
    }

    private async Task DoSetAsync(string key, string value)
    {
        EndPoint endpoint = await GetAndWaitForLeader();
        if (!cluster.LeadershipToken.IsCancellationRequested)
        {
            var simplePersistentState = serviceProvider.GetRequiredService<SlimPersistentState>();
            await Endpoints.AddKeyValueCommand(simplePersistentState, key, value, cluster, new CancellationTokenSource());
        }
        else
        {
            MultipartFormDataContent multipart = new();
            multipart.Add(new StringContent(value), key);

            HttpResponseMessage response =
                await httpClient.PostAsync(new Uri($"{endpoint}SlimData/AddKeyValue"), multipart);
            if ((int)response.StatusCode >= 500)
            {
                throw new DataException("Error in calling SlimData HTTP Service");
            }
        }
    }

    public async Task HashSetAsync(string key, IDictionary<string, string> values)
    {
        await Retry.Do(() =>DoHashSetAsync(key, values), TimeSpan.FromSeconds(1), 5);
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

            HttpResponseMessage response =
                await httpClient.PostAsync(new Uri($"{endpoint}SlimData/AddHashset"), multipart);
            if ((int)response.StatusCode >= 500)
            {
                throw new DataException("Error in calling SlimData HTTP Service");
            }
        }
    }


    public async Task<IDictionary<string, string>> HashGetAllAsync(string key)
    {
        return await Retry.Do(() =>DoHashGetAllAsync(key), TimeSpan.FromSeconds(1), 5);
    }

    private async Task<IDictionary<string, string>> DoHashGetAllAsync(string key)
    {
        await GetAndWaitForLeader();
        if (cluster.LeadershipToken.IsCancellationRequested)
        {
            if (cluster.Lease is null or { IsExpired: true })
            {
                await cluster.ApplyReadBarrierAsync();
            }
        }

        SupplierPayload data = SimplePersistentState.Invoke();
        return data.Hashsets.TryGetValue(key, out Dictionary<string, string>? value)
            ? (IDictionary<string, string>)value
            : new Dictionary<string, string>();
    }

    public async Task ListLeftPushAsync(string key, string field)
    {
        await Retry.Do(() =>DoListLeftPushAsync(key, field), TimeSpan.FromSeconds(1), 5);
    }

    private async Task DoListLeftPushAsync(string key, string field)
    {
        EndPoint endpoint = await GetAndWaitForLeader();
        if (!cluster.LeadershipToken.IsCancellationRequested)
        {
            var simplePersistentState = serviceProvider.GetRequiredService<SlimPersistentState>();
            await Endpoints.ListLeftPushCommand(simplePersistentState, key, field, cluster, new CancellationTokenSource());
        }
        else
        {
            HttpRequestMessage request = new(HttpMethod.Post, new Uri($"{endpoint}SlimData/ListLeftPush"));
            MultipartFormDataContent multipart = new();
            multipart.Add(new StringContent(field), key);
            request.Content = multipart;
            HttpResponseMessage response = await httpClient.SendAsync(request);
            if ((int)response.StatusCode >= 500)
            {
                throw new DataException("Error in calling SlimData HTTP Service");
            }
        }
    }

    public async Task<IList<string>> ListRightPopAsync(string key, int count = 1)
    {
        return await Retry.Do(() =>DoListRightPopAsync(key, count), TimeSpan.FromSeconds(1), 5);
    }

    private async Task<IList<string>> DoListRightPopAsync(string key, int count = 1)
    {
        EndPoint endpoint = await GetAndWaitForLeader();
        if (!cluster.LeadershipToken.IsCancellationRequested)
        {
            var simplePersistentState = serviceProvider.GetRequiredService<SlimPersistentState>();
            var result = await Endpoints.ListRightPopCommand(simplePersistentState, key, count, cluster, new CancellationTokenSource());
            return result;
        }
        else
        {
            HttpRequestMessage request = new(HttpMethod.Post, new Uri($"{endpoint}SlimData/ListRightPop"));
            MultipartFormDataContent multipart = new();
            multipart.Add(new StringContent(count.ToString()), key);

            request.Content = multipart;
            HttpResponseMessage response = await httpClient.SendAsync(request);
            if ((int)response.StatusCode >= 500)
            {
                throw new DataException("Error in calling SlimData HTTP Service");
            }

            string json = await response.Content.ReadAsStringAsync();
            List<string>? result = !string.IsNullOrEmpty(json)
                ? JsonSerializer.Deserialize(json,
                    ListStringSerializerContext.Default.ListString)
                : new List<string>();

            return result ?? new List<string>();
        }
    }

    public async Task<long> ListLengthAsync(string key)
    {
        return await Retry.Do(() =>DoListLengthAsync(key), TimeSpan.FromSeconds(1), 5);
    }

    private async Task<long> DoListLengthAsync(string key)
    {
        await GetAndWaitForLeader();
        if (cluster.LeadershipToken.IsCancellationRequested)
        {
            if (cluster.Lease is null or { IsExpired: true })
            {
                await cluster.ApplyReadBarrierAsync();
            }
        }
        SupplierPayload data = SimplePersistentState.Invoke();
        long result = data.Queues.TryGetValue(key, out List<string>? value) ? value.Count : 0L;
        return result;
    }

    private async Task<EndPoint> GetAndWaitForLeader()
    {
        int numberWaitMaximum = 6;
        while (cluster.Leader == null && numberWaitMaximum > 0)
        {
            await Task.Delay(500);
            numberWaitMaximum--;
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
        int maxAttemptCount = 3)
    {
        var exceptions = new List<Exception>();

        for (int attempted = 0; attempted < maxAttemptCount; attempted++)
        {
            try
            {
                if (attempted > 0)
                {
                    Thread.Sleep(retryInterval);
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
