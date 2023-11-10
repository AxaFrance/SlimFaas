using System.Data;
using DotNext;
using Newtonsoft.Json;
using RaftNode;

namespace SlimFaas;
#pragma warning disable CA2252

public class SlimDataService : IRedisService
{
    private readonly HttpClient _httpClient;
    private readonly ISupplier<SupplierPayload> _simplePersistentState;

    public SlimDataService(HttpClient httpClient, SimplePersistentState simplePersistentState)
    {
        _httpClient = httpClient;
        _simplePersistentState =  (ISupplier<SupplierPayload>)simplePersistentState;
    }

    public Task<string> GetAsync(string key) {

        var data = _simplePersistentState.Invoke();
        return data.KeyValues.TryGetValue(key, out var value) ? Task.FromResult(value) : Task.FromResult(string.Empty);
    }

    public async Task SetAsync(string key, string value)
    {
        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(value), key);

        var response = await _httpClient.PostAsync(new Uri("http://localhost:3262/AddKeyValue"), multipart);
        if ((int)response.StatusCode >= 500)
        {
            throw new DataException("Error in Redis Service");
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

        var response = await _httpClient.PostAsync(new Uri("http://localhost:3262/AddHashset"), multipart);
        if ((int)response.StatusCode >= 500)
        {
            throw new DataException("Error in Redis Service");
        }
    }

    public Task<IDictionary<string, string>> HashGetAllAsync(string key)  {
        var data = _simplePersistentState.Invoke();
        return data.Hashsets.TryGetValue(key, out var value) ? Task.FromResult((IDictionary<string, string>)value) : Task.FromResult((IDictionary<string, string>)new Dictionary<string, string>());
    }

    public Task ListLeftPushAsync(string key, string field) {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("http://localhost:3262/ListLeftPush"));
        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(field), key);
        request.Content = multipart;
        var response = _httpClient.SendAsync(request);
        return Task.CompletedTask;
    }

    public async Task<IList<string>> ListRightPopAsync(string key, long count = 1)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("http://localhost:3262/ListRightPop"));
        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(count.ToString()), key);

        request.Content = multipart;
        var response = await _httpClient.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(json) ? new List<string>() : JsonConvert.DeserializeObject<IList<string>>(json);
    }

    public Task<long> ListLengthAsync(string key) {
        var data = _simplePersistentState.Invoke();
        var result = data.Queues.TryGetValue(key, out var value) ? Task.FromResult((long)value.Count) : Task.FromResult(0L);
        return result;
    }
}
#pragma warning restore CA2252
