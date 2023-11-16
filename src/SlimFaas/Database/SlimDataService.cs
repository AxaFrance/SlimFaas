using System.Data;
using DotNext;
using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft;
using Newtonsoft.Json;
using RaftNode;

namespace SlimFaas;
#pragma warning disable CA2252

public class SlimDataService(HttpClient httpClient, SimplePersistentState simplePersistentState, IRaftCluster cluster)
    : IDatabaseService
{
    private readonly ISupplier<SupplierPayload> _simplePersistentState = simplePersistentState;

    private IClusterMember? GetAndWaitForLeader()
    {
       /* var numberWaitMaximum = 10;
        while (cluster.Leader == null && numberWaitMaximum > 0)
        {
            Thread.Sleep(300);
            numberWaitMaximum--;
        }

        if (cluster.Leader == null)
        {
            throw new DataException("Notleader found");
        }*/
        return cluster.Leader;
    }

    public async Task<string> GetAsync(string key) {

        await cluster.ApplyReadBarrierAsync();
        var data = _simplePersistentState.Invoke();
        return data.KeyValues.TryGetValue(key, out var value) ? value: string.Empty;
    }

    public async Task SetAsync(string key, string value)
    {
        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(value), key);

        var response = await httpClient.PostAsync(new Uri($"{GetAndWaitForLeader()?.EndPoint}AddKeyValue"), multipart);
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

        var response = await httpClient.PostAsync(new Uri($"{GetAndWaitForLeader()?.EndPoint}AddHashset"), multipart);
        if ((int)response.StatusCode >= 500)
        {
            throw new DataException("Error in calling SlimData HTTP Service");
        }
    }

    public async Task<IDictionary<string, string>> HashGetAllAsync(string key)  {
        await cluster.ApplyReadBarrierAsync();
        var data = _simplePersistentState.Invoke();
        return data.Hashsets.TryGetValue(key, out var value) ? (IDictionary<string, string>)value : new Dictionary<string, string>();
    }

    public async Task ListLeftPushAsync(string key, string field) {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{GetAndWaitForLeader()?.EndPoint}ListLeftPush"));
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
            var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{GetAndWaitForLeader()?.EndPoint}ListRightPop"));
            var multipart = new MultipartFormDataContent();
            multipart.Add(new StringContent(count.ToString()), key);

            request.Content = multipart;
            var response = await httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if ((int)response.StatusCode >= 500)
            {
                throw new DataException("Error in calling SlimData HTTP Service");
            }
            return string.IsNullOrEmpty(json) ? new List<string>() : JsonConvert.DeserializeObject<IList<string>>(json);
    }

    public async Task<long> ListLengthAsync(string key) {
        await cluster.ApplyReadBarrierAsync();
        var data = _simplePersistentState.Invoke();
        var result = data.Queues.TryGetValue(key, out var value) ? (long)value.Count :0L;
        return result;
    }
}
#pragma warning restore CA2252
