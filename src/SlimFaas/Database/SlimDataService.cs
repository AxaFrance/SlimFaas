using DotNext;
using Newtonsoft.Json;
using RaftNode;

namespace SlimFaas;
#pragma warning disable CA2252

public class SlimDataService : IRedisService
{
    private readonly HttpClient _httpClient;
    private readonly SimplePersistentState _simplePersistentState;

    public SlimDataService(HttpClient httpClient, SimplePersistentState simplePersistentState)
    {
        _httpClient = httpClient;
        _simplePersistentState = simplePersistentState;
    }

    public Task<string> GetAsync(string key) {

        var data = ((ISupplier<SupplierPayload>)_simplePersistentState).Invoke();
        return data.KeyValues.TryGetValue(key, out var value) ? Task.FromResult(value) : Task.FromResult(string.Empty);
    }

    public async Task SetAsync(string key, string value)
    {
        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(key), value);

        var response = await _httpClient.PostAsync(new Uri("http://localhost:3262/AddKeyValue"), multipart);
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
    }

    public Task<IDictionary<string, string>> HashGetAllAsync(string key)  {
        var data = ((ISupplier<SupplierPayload>)_simplePersistentState).Invoke();
        return data.Hashsets.TryGetValue(key, out var value) ? Task.FromResult((IDictionary<string, string>)value) : Task.FromResult((IDictionary<string, string>)new Dictionary<string, string>());
    }

    public Task ListLeftPushAsync(string key, string field) => throw new NotImplementedException();

    public async Task<IList<string>> ListRightPopAsync(string key, long count = 1)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("http://localhost:3262/ListRightPop"));
        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(count.ToString()), key);

        request.Content = multipart;
        var response = await _httpClient.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(json))
        {
            return new List<string>();
        }
        return JsonConvert.DeserializeObject<IList<string>>(json);
    }

    public Task<long> ListLengthAsync(string key) {

        var data = ((ISupplier<SupplierPayload>)_simplePersistentState).Invoke();
        return data.Queues.TryGetValue(key, out var value) ? Task.FromResult((long)value.Count) : Task.FromResult(0L);
    }
}
#pragma warning restore CA2252
