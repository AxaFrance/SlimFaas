namespace SlimFaas;

public interface ISendClient
{
    Task<HttpResponseMessage> SendHttpRequestAsync(CustomRequest customRequest, HttpContext? context = null);
}

public class SendClient : ISendClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseFunctionUrl;

    public SendClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _baseFunctionUrl =
            Environment.GetEnvironmentVariable("BASE_FUNCTION_URL") ?? "http://localhost:5123/"; //""http://{function_name}:8080";
    }
    
    private void CopyFromOriginalRequestContentAndHeaders(CustomRequest context, HttpRequestMessage requestMessage)
    {
        var requestMethod = context.Method;

        if (!HttpMethods.IsGet(requestMethod) &&
            !HttpMethods.IsHead(requestMethod) &&
            !HttpMethods.IsDelete(requestMethod) &&
            !HttpMethods.IsTrace(requestMethod) && 
            context.Body != null)
        {
            var streamContent = new StreamContent(new MemoryStream(context.Body));
            requestMessage.Content = streamContent;
        }

        foreach (var header in context.Headers)
        {
            requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Values);
        }
    }
    
    private HttpRequestMessage CreateTargetMessage(CustomRequest context, Uri targetUri)
    {
        var requestMessage = new HttpRequestMessage();
        CopyFromOriginalRequestContentAndHeaders(context, requestMessage);

        requestMessage.RequestUri = targetUri;
        requestMessage.Headers.Host = targetUri.Host;
        requestMessage.Method = GetMethod(context.Method);

        return requestMessage;
    }
    
    private static HttpMethod GetMethod(string method)
    {
        if (HttpMethods.IsDelete(method)) return HttpMethod.Delete;
        if (HttpMethods.IsGet(method)) return HttpMethod.Get;
        if (HttpMethods.IsHead(method)) return HttpMethod.Head;
        if (HttpMethods.IsOptions(method)) return HttpMethod.Options;
        if (HttpMethods.IsPost(method)) return HttpMethod.Post;
        if (HttpMethods.IsPut(method)) return HttpMethod.Put;
        if (HttpMethods.IsTrace(method)) return HttpMethod.Trace;
        return new HttpMethod(method);
    }
    
    public async Task<HttpResponseMessage> SendHttpRequestAsync(CustomRequest customRequest, HttpContext? context = null)
    {
        var functionUrl = _baseFunctionUrl;
        var url = functionUrl.Replace("{function_name}", customRequest.FunctionName) + customRequest.Path +
                  customRequest.Query;
        var targetRequestMessage = CreateTargetMessage(customRequest, new Uri(url));
        if (context != null)
        {
            return await _httpClient.SendAsync(targetRequestMessage,
                HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            
        }
        return await _httpClient.SendAsync(targetRequestMessage,
            HttpCompletionOption.ResponseHeadersRead);
    }
}