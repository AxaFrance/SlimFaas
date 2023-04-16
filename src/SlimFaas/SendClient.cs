using System.Text;
using SlimFaas;

public class SendClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseFunctionUrl;

    public SendClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _baseFunctionUrl =
            Environment.GetEnvironmentVariable("BASE_FUNCTION_URL") ?? "http://localhost:5123/"; //""http://{function_name}:8080";
    }
    
    public async Task<HttpResponseMessage> SendHttpRequestAsync(CustomRequest customRequest)
    {
        var functionUrl = _baseFunctionUrl;
        var url = functionUrl.Replace("{function_name}", customRequest.FunctionName) + customRequest.Path +
                  customRequest.Query;

        switch (customRequest.Method)
        {
            case "GET":
            case "DELETE":
            {
                var response = await _httpClient.GetAsync(url);
                return response;
            }
            case "POST":
            case "PUT":
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
            
                var response = await _httpClient.SendAsync(httpRequestMessage);
                return response;
            }
            default:
                throw new NotImplementedException("Method not implemented");
        }
    }
}