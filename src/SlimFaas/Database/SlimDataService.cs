using System.Data;
using System.Net;
using DotNext;
using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft;
using Newtonsoft.Json;
using RaftNode;

namespace SlimFaas;
#pragma warning disable CA2252

public class SlimDataService(HttpClient httpClient, IServiceProvider serviceProvider, IRaftCluster cluster)
    : IDatabaseService
{
    private  ISupplier<SupplierPayload> SimplePersistentState
    {
      get {
         return serviceProvider.GetRequiredService<SlimPersistentState>();
      }
    }

    private async Task<EndPoint> GetAndWaitForLeader()
    {
        var numberWaitMaximum = 6;
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

    public async Task<string> GetAsync(string key) {
        await GetAndWaitForLeader();
        await cluster.ApplyReadBarrierAsync();
        var data = SimplePersistentState.Invoke();
        return data.KeyValues.TryGetValue(key, out var value) ? value: string.Empty;
    }

    public async Task SetAsync(string key, string value)
    {
        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(value), key);

        var endpoint = await GetAndWaitForLeader();
        var response = await httpClient.PostAsync(new Uri($"{endpoint}SlimData/AddKeyValue"), multipart);
        if ((int)response.StatusCode >= 500)
        {
            throw new DataException("Error in calling SlimData HTTP Service");
        }
    }

    public async Task HashSetAsync(string key, IDictionary<string, string> values)
    {
        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(key), "______key_____");
        foreach (KeyValuePair<string,string> value in values)
        {
            multipart.Add(new StringContent(value.Value), value.Key);
        }

        var endpoint = await GetAndWaitForLeader();
        var response = await httpClient.PostAsync(new Uri($"{endpoint}SlimData/AddHashset"), multipart);
        if ((int)response.StatusCode >= 500)
        {
            throw new DataException("Error in calling SlimData HTTP Service");
        }
    }

    public async Task<IDictionary<string, string>> HashGetAllAsync(string key)  {
await GetAndWaitForLeader();
        await cluster.ApplyReadBarrierAsync();
        var data = SimplePersistentState.Invoke();
        return data.Hashsets.TryGetValue(key, out var value) ? (IDictionary<string, string>)value : new Dictionary<string, string>();
    }

    public async Task ListLeftPushAsync(string key, string field) {
        var endpoint = await GetAndWaitForLeader();
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{endpoint}SlimData/ListLeftPush"));
        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(field), key);
        request.Content = multipart;
        var response = await httpClient.SendAsync(request);
        if ((int)response.StatusCode >= 500)
        {
            throw new DataException("Error in calling SlimData HTTP Service");
        }
    }

    public async Task<IList<string>> ListRightPopAsync(string key, long count = 1)
    {
        var endpoint = await GetAndWaitForLeader();
            var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{endpoint}SlimData/ListRightPop"));
            var multipart = new MultipartFormDataContent();
            multipart.Add(new StringContent(count.ToString()), key);

            request.Content = multipart;
            var response = await httpClient.SendAsync(request);
            if ((int)response.StatusCode >= 500)
            {
                throw new DataException("Error in calling SlimData HTTP Service");
            }
            string json = await response.Content.ReadAsStringAsync();
            return string.IsNullOrEmpty(json) ? new List<string>() : JsonConvert.DeserializeObject<IList<string>>(json);
    }

    public async Task<long> ListLengthAsync(string key) {
await GetAndWaitForLeader();
        await cluster.ApplyReadBarrierAsync();
        var data = SimplePersistentState.Invoke();
        var result = data.Queues.TryGetValue(key, out var value) ? (long)value.Count :0L;
        return result;
    }
}
#pragma warning restore CA2252
