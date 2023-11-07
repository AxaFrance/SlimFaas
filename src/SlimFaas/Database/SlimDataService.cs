namespace SlimFaas;

public class SlimDataService : IRedisService
{
    private readonly HttpClient _httpClient;

    public SlimDataService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<string> GetAsync(string key) {

        var response = _httpClient.GetAsync($"http://localhost:3262/api/redis/{key}").Result;
        return response.Content.ReadAsStringAsync();
    }

    public async Task SetAsync(string key, string value)
    {
        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(key), value);

        var httpClient = new HttpClient();
        var response = await httpClient.PostAsync(new Uri("http://localhost:55530"), multipart);
    }

    public Task HashSetAsync(string key, IDictionary<string, string> values)
    {
        throw new NotImplementedException();
    }

    public Task<IDictionary<string, string>> HashGetAllAsync(string key)  {
        throw new NotImplementedException();
    }

    public Task ListLeftPushAsync(string key, string field) => throw new NotImplementedException();

    public Task<IList<string>> ListRightPopAsync(string key, long count = 1) => throw new NotImplementedException();

    public Task<long> ListLengthAsync(string key) => throw new NotImplementedException();
}
