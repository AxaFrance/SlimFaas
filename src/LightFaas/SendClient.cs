using System.Text;

public class SendClient
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly string _baseFunctionUrl;

    public SendClient(IHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
        _baseFunctionUrl =
            Environment.GetEnvironmentVariable("BASE_FUNCTION_URL") ?? "http://localhost:5123/"; //""http://{function_name}:8080";
    }
    
    public async Task<HttpResponseMessage> SendHttpRequestAsync(CustomRequest customRequest)
    {
        var functionUrl = _baseFunctionUrl;
        var url = functionUrl.Replace("{function_name}", customRequest.FunctionName) + customRequest.Path +
                  customRequest.Query;

        if (customRequest.Method == "GET" || customRequest.Method == "DELETE")
        {
            var client = _clientFactory.CreateClient();
            var response = await client.GetAsync(url);
            return response;
        }

        if (customRequest.Method == "POST" || customRequest.Method == "PUT")
        {
            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, url);
            foreach (var customRequestHeader in customRequest.Headers)
            {
                foreach (var value in customRequestHeader.Values)
                {
                    if (customRequestHeader.Key == "Content-Length" || customRequestHeader.Key == "Content-Type")
                        continue;
                    httpRequestMessage.Headers.Add(customRequestHeader.Key, value);
                }
            }
            if (customRequest.ContentType == "multipart/form-data")
            {
                var requestContent = new MultipartFormDataContent();
                foreach (var formData in customRequest.Form)
                {
                    foreach (var value in formData.Values)
                    {
                        if (value != null)
                        {
                            requestContent.Add(new StringContent(value), formData.Key);
                        }
                    }
                }

                foreach (var requestFormFile in customRequest.FormFiles)
                {
                    var streamContent = new StreamContent(new MemoryStream(requestFormFile.Value));
                    requestContent.Add(streamContent, requestFormFile.Key, requestFormFile.Filename);
                }

                httpRequestMessage.Content = requestContent;
            }
            else if(customRequest.ContentType == "application/json")
            {
                httpRequestMessage.Content = new StringContent(customRequest.Body, Encoding.UTF8, "application/json");
                httpRequestMessage.Method = new HttpMethod(customRequest.Method);
            }
            else
            {
                httpRequestMessage.Content = new StringContent(customRequest.Body);
                httpRequestMessage.Method = new HttpMethod(customRequest.Method);
            }

            var client = _clientFactory.CreateClient();
            var response = await client.SendAsync(httpRequestMessage);
            return response;
        }

        throw new NotImplementedException("Method not implemented");
    }
}