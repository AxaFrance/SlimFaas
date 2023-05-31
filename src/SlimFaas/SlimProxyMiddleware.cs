using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using SlimFaas;

public class SlimProxyMiddleware 
{
    private readonly RequestDelegate _next;
    private readonly IQueue _queue;

    public SlimProxyMiddleware(RequestDelegate next, IQueue queue)
    {
        _next = next;
        _queue = queue;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
    public async Task InvokeAsync(HttpContext context, ILogger<SlimProxyMiddleware> faasLogger, HistoryHttpMemoryService historyHttpService, SendClient sendClient)
    {
        IList<CustomHeader> customHeaders = new List<CustomHeader>();
        var contextRequest = context.Request;
        foreach (var headers in contextRequest.Headers)
        {
            var customHeader = new CustomHeader(headers.Key, headers.Value.ToArray());
            customHeaders.Add(customHeader);
        }
    
        var requestMethod = contextRequest.Method;
        var requestPath = contextRequest.Path;
        var requestQueryString = contextRequest.QueryString;
        var functionBeginPath = FunctionBeginPath(requestPath);

        if(!string.IsNullOrEmpty(functionBeginPath))
        {
            var pathString = requestPath.ToUriComponent();
            var paths = pathString.Split("/");
            if(paths.Length > 2) {
                var functionName = paths[2];
                var functionPath = pathString.Replace(functionBeginPath + functionName, "");
                faasLogger.LogInformation("{Method}: {Function}{UriComponent}", requestMethod, pathString, requestQueryString.ToUriComponent());

                byte[]? requestBodyBytes = null;
                if (!HttpMethods.IsGet(requestMethod) &&
                    !HttpMethods.IsHead(requestMethod) &&
                    !HttpMethods.IsDelete(requestMethod) &&
                    !HttpMethods.IsTrace(requestMethod))
                {
                    using var streamContent = new StreamContent(context.Request.Body);
                    using var memoryStream = new MemoryStream();
                    await streamContent.CopyToAsync(memoryStream);
                    requestBodyBytes = memoryStream.ToArray();
                }
                
                var customRequest = new CustomRequest()
                {
                    Headers = customHeaders,
                    FunctionName = functionName,
                    Path = functionPath,
                    Body = requestBodyBytes,
                    Query= requestQueryString.ToUriComponent(),
                    Method = requestMethod,
                };

                var contextResponse = context.Response;
                if (functionBeginPath == AsyncFunction)
                {
                    await _queue.EnqueueAsync(functionName, JsonSerializer.Serialize(customRequest, CustomRequestSerializerContext.Default.CustomRequest));
                    contextResponse.StatusCode = 202;
                    return;
                }
                historyHttpService.SetTickLastCall(functionName, DateTime.Now.Ticks);
                var responseMessagePromise = sendClient.SendHttpRequestAsync(customRequest);
                var counterLimit = 100;
                while (responseMessagePromise.IsCompleted)
                {
                    await Task.Delay(10, context.RequestAborted);
                    counterLimit--;
                    if (counterLimit <= 0)
                    {
                        historyHttpService.SetTickLastCall(functionName, DateTime.Now.Ticks);        
                    }

                    counterLimit = 100;
                }
                historyHttpService.SetTickLastCall(functionName, DateTime.Now.Ticks);
                using var responseMessage = responseMessagePromise.Result;
                context.Response.StatusCode = (int)responseMessage.StatusCode;
                CopyFromTargetResponseHeaders(context, responseMessage);
                await responseMessage.Content.CopyToAsync(context.Response.Body);
                return;
            }
        }
        
        await _next(context);
    }
    const string AsyncFunction = "/async-function";
    const string Function = "/function";
    private static string FunctionBeginPath(PathString path)
    {
        var functionBeginPath = String.Empty;
        if (path.StartsWithSegments(AsyncFunction))
        {
            functionBeginPath = $"{AsyncFunction}/";
        }
        else
        {
            
            if (path.StartsWithSegments(Function))
            {
                functionBeginPath = $"{Function}/";
            }
        }

        return functionBeginPath;
    }
    
    private void CopyFromTargetResponseHeaders(HttpContext context, HttpResponseMessage responseMessage)
    {
        foreach (var header in responseMessage.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in responseMessage.Content.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        context.Response.Headers.Remove("transfer-encoding");
    }
}

public class ReverseProxyMiddleware
  {
    private static readonly HttpClient _httpClient = new HttpClient();
    private readonly RequestDelegate _nextMiddleware;

    public ReverseProxyMiddleware(RequestDelegate nextMiddleware)
    {
      _nextMiddleware = nextMiddleware;
    }

    public async Task Invoke(HttpContext context)
    {
      var targetUri = BuildTargetUri(context.Request);

      if (targetUri != null)
      {
        var targetRequestMessage = CreateTargetMessage(context, targetUri);

        using (var responseMessage = await _httpClient.SendAsync(targetRequestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted))
        {
          context.Response.StatusCode = (int)responseMessage.StatusCode;
          CopyFromTargetResponseHeaders(context, responseMessage);
          await responseMessage.Content.CopyToAsync(context.Response.Body);
        }
        return;
      }
      await _nextMiddleware(context);
    }

    private HttpRequestMessage CreateTargetMessage(HttpContext context, Uri targetUri)
    {
      var requestMessage = new HttpRequestMessage();
      CopyFromOriginalRequestContentAndHeaders(context, requestMessage);

      requestMessage.RequestUri = targetUri;
      requestMessage.Headers.Host = targetUri.Host;
      requestMessage.Method = GetMethod(context.Request.Method);

      return requestMessage;
    }

    private void CopyFromOriginalRequestContentAndHeaders(HttpContext context, HttpRequestMessage requestMessage)
    {
      var requestMethod = context.Request.Method;

      if (!HttpMethods.IsGet(requestMethod) &&
        !HttpMethods.IsHead(requestMethod) &&
        !HttpMethods.IsDelete(requestMethod) &&
        !HttpMethods.IsTrace(requestMethod))
      {
        var streamContent = new StreamContent(context.Request.Body);
        requestMessage.Content = streamContent;
      }

      foreach (var header in context.Request.Headers)
      {
        requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
      }
    }

    private void CopyFromTargetResponseHeaders(HttpContext context, HttpResponseMessage responseMessage)
    {
      foreach (var header in responseMessage.Headers)
      {
        context.Response.Headers[header.Key] = header.Value.ToArray();
      }

      foreach (var header in responseMessage.Content.Headers)
      {
        context.Response.Headers[header.Key] = header.Value.ToArray();
      }
      context.Response.Headers.Remove("transfer-encoding");
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

    private Uri BuildTargetUri(HttpRequest request)
    {
      Uri targetUri = null;

      if (request.Path.StartsWithSegments("/googleforms", out var remainingPath))
      {
        targetUri = new Uri("https://docs.google.com/forms" + remainingPath);
      }

      return targetUri;
    }
  }