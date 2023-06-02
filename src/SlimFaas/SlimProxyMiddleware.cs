using System.Text.Json;

namespace SlimFaas;

public enum FunctionType
{
    Sync,
    Async,
    NotAFunction
}

public class SlimProxyMiddleware 
{
    private readonly RequestDelegate _next;
    private readonly IQueue _queue;

    public SlimProxyMiddleware(RequestDelegate next, IQueue queue)
    {
        _next = next;
        _queue = queue;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<SlimProxyMiddleware> faasLogger, HistoryHttpMemoryService historyHttpService, ISendClient sendClient)
    {
        var contextRequest = context.Request;
        var (functionPath, functionName, functionType) = GetFunctionInfo(faasLogger, contextRequest);
        if(functionType == FunctionType.NotAFunction)
        {
            await _next(context);
            return;
        }
        var customRequest = await InitCustomRequest(context, contextRequest, functionName, functionPath);
        if (functionType == FunctionType.Async)
        {
            var contextResponse = context.Response;
            await BuildAsyncResponse(functionName, customRequest, contextResponse);
            return;
        }
        await BuildSyncResponse(context, historyHttpService, sendClient, functionName, customRequest);
    }

    private async Task BuildAsyncResponse(string functionName, CustomRequest customRequest, HttpResponse contextResponse)
    {
        await _queue.EnqueueAsync(functionName,
            JsonSerializer.Serialize(customRequest, CustomRequestSerializerContext.Default.CustomRequest));
        contextResponse.StatusCode = 202;
    }

    private async Task BuildSyncResponse(HttpContext context, HistoryHttpMemoryService historyHttpService,
        ISendClient sendClient, string functionName, CustomRequest customRequest)
    {
        historyHttpService.SetTickLastCall(functionName, DateTime.Now.Ticks);
        var responseMessagePromise = sendClient.SendHttpRequestAsync(customRequest);
        var counterLimit = 100;
        // TODO manage request Aborded
        while (!responseMessagePromise.IsCompleted)
        {
            await Task.Delay(10);
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
    }

    private static async Task<CustomRequest> InitCustomRequest(HttpContext context, HttpRequest contextRequest,
        string functionName, string functionPath)
    {
        IList<CustomHeader> customHeaders = contextRequest.Headers.Select(headers => new CustomHeader(headers.Key, headers.Value.ToArray())).ToList();

        var requestMethod = contextRequest.Method;
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

        var requestQueryString = contextRequest.QueryString;
        var customRequest = new CustomRequest
        {
            Headers = customHeaders,
            FunctionName = functionName,
            Path = functionPath,
            Body = requestBodyBytes,
            Query = requestQueryString.ToUriComponent(),
            Method = requestMethod,
        };
        return customRequest;
    }

    private record FunctionInfo(string FunctionPath, string FunctionName, FunctionType FunctionType = FunctionType.NotAFunction);

    private const string AsyncFunction = "/async-function";
    private const string Function = "/function";

    private static FunctionInfo GetFunctionInfo(ILogger<SlimProxyMiddleware> faasLogger, HttpRequest contextRequest)
    {
        var requestMethod = contextRequest.Method;
        var requestPath = contextRequest.Path;
        var requestQueryString = contextRequest.QueryString;
        var functionBeginPath = FunctionBeginPath(requestPath);
        if (string.IsNullOrEmpty(functionBeginPath))
        {
            return new FunctionInfo(String.Empty, String.Empty);
        }
        var pathString = requestPath.ToUriComponent();
        var paths = pathString.Split("/");
        if (paths.Length <= 2)
        {
            return new FunctionInfo(String.Empty, String.Empty);
        }
        var functionName = paths[2];
        var functionPath = pathString.Replace($"{functionBeginPath}/{functionName}", "");
        faasLogger.LogInformation("{Method}: {Function}{UriComponent}", requestMethod, pathString,
            requestQueryString.ToUriComponent());
        return new FunctionInfo(functionPath, functionName, functionBeginPath == AsyncFunction ? FunctionType.Async : FunctionType.Sync);
    }

    private static string FunctionBeginPath(PathString path)
    {
        var functionBeginPath = String.Empty;
        if (path.StartsWithSegments(AsyncFunction))
        {
            functionBeginPath = $"{AsyncFunction}";
        }
        else
        {
            if (path.StartsWithSegments(Function))
            {
                functionBeginPath = $"{Function}";
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