using System.Text.Json;

namespace SlimFaas;

public enum FunctionType
{
    Sync,
    Async,
    Wake,
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
        var contextResponse = context.Response;
        switch (functionType)
        {
            case FunctionType.NotAFunction:
                await _next(context);
                return;
            case FunctionType.Wake:
                historyHttpService.SetTickLastCall(functionName, DateTime.Now.Ticks);
                contextResponse.StatusCode = 200;
                return;
            case FunctionType.Sync:
                await BuildSyncResponse(context, historyHttpService, sendClient, functionName, functionPath);
                return;
            default:
            {
                var customRequest = await InitCustomRequest(context, contextRequest, functionName, functionPath);
                await BuildAsyncResponse(functionName, customRequest, contextResponse);
                break;
            }
        }
    }

    private async Task BuildAsyncResponse(string functionName, CustomRequest customRequest, HttpResponse contextResponse)
    {
        await _queue.EnqueueAsync(functionName,
            JsonSerializer.Serialize(customRequest, CustomRequestSerializerContext.Default.CustomRequest));
        contextResponse.StatusCode = 202;
    }

    private async Task BuildSyncResponse(HttpContext context, HistoryHttpMemoryService historyHttpService,
        ISendClient sendClient, string functionName, string functionPath)
    {
        historyHttpService.SetTickLastCall(functionName, DateTime.Now.Ticks);
        var responseMessagePromise = sendClient.SendHttpRequestSync(context, functionName, functionPath, context.Request.QueryString.ToUriComponent());
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
        var responseMessage = responseMessagePromise.Result;
        context.Response.StatusCode = (int)responseMessage.StatusCode;
        CopyFromTargetResponseHeaders(context, responseMessage);
        await responseMessage.Content.CopyToAsync(context.Response.Body);
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
    private const string WakeFunction = "/wake-function";
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

        var functionType = functionBeginPath switch
        {
            AsyncFunction => FunctionType.Async,
            Function => FunctionType.Sync,
            WakeFunction => FunctionType.Wake,
            _ => FunctionType.NotAFunction
        };
        return new FunctionInfo(functionPath, functionName, functionType);
    }

    private static string FunctionBeginPath(PathString path)
    {
        var functionBeginPath = String.Empty;
        if (path.StartsWithSegments(AsyncFunction)) {
            functionBeginPath = $"{AsyncFunction}";
        } else if (path.StartsWithSegments(Function)) {
            functionBeginPath = $"{Function}";
        } else if (path.StartsWithSegments(WakeFunction)) {
            functionBeginPath = $"{WakeFunction}";
        }

        return functionBeginPath;
    }
    
   
}