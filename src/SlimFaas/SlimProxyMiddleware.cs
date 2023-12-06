using System.Text.Json.Serialization;
using SlimFaas.Kubernetes;

namespace SlimFaas;

public enum FunctionType
{
    Sync,
    Async,
    Wake,
    Status,
    NotAFunction
}

public record FunctionStatus(int NumberReady, int numberRequested);

[JsonSerializable(typeof(FunctionStatus))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class FunctionStatusSerializerContext : JsonSerializerContext
{

}


public class SlimProxyMiddleware(RequestDelegate next, ISlimFaasQueue slimFaasQueue, ILogger<SlimProxyMiddleware> logger, int timeoutWaitWakeSyncFunctionMilliSecond = EnvironmentVariables.SlimProxyMiddlewareTimeoutWaitWakeSyncFunctionMilliSecondsDefault)
{
    private readonly int _timeoutMaximumWaitWakeSyncFunctionMilliSecond = EnvironmentVariables.ReadInteger(logger, EnvironmentVariables.TimeMaximumWaitForAtLeastOnePodStartedForSyncFunction, timeoutWaitWakeSyncFunctionMilliSecond);
    private readonly int _slimFaasPort = EnvironmentVariables.ReadInteger(logger, EnvironmentVariables.SlimFaasPort, EnvironmentVariables.SlimFaasPortDefault);

    public async Task InvokeAsync(HttpContext context,
        HistoryHttpMemoryService historyHttpService, ISendClient sendClient, IReplicasService replicasService)
    {
        if(context.Request.Host.Port != _slimFaasPort || (!context.Request.Host.Port.HasValue && _slimFaasPort != 80))
        {
            await next(context);
            return;
        }
        var contextRequest = context.Request;
        var (functionPath, functionName, functionType) = GetFunctionInfo(logger, contextRequest);
        var contextResponse = context.Response;
        switch (functionType)
        {
            case FunctionType.NotAFunction:
                await next(context);
                return;
            case FunctionType.Wake:
                BuildWakeResponse(historyHttpService, replicasService, functionName, contextResponse);
                return;
            case FunctionType.Status:
                 BuildStatusResponse(replicasService, functionName, contextResponse);
                 return;
            case FunctionType.Sync:
                await BuildSyncResponseAsync(context, historyHttpService, sendClient, replicasService, functionName, functionPath);
                return;
            case FunctionType.Async:
            default:
            {
                var customRequest = await InitCustomRequest(context, contextRequest, functionName, functionPath);
                await BuildAsyncResponseAsync(replicasService ,functionName, customRequest, contextResponse);
                break;
            }
        }
    }

    private static void BuildStatusResponse(IReplicasService replicasService,
        string functionName, HttpResponse contextResponse)
    {
        var function = SearchFunction(replicasService, functionName);
        if (function != null)
        {
            var functionDeploymentInformation = replicasService.Deployments.Functions.FirstOrDefault(f => f.Deployment == functionName);
            var numberReady = functionDeploymentInformation == null ? 0 : functionDeploymentInformation.Pods.Count(p => p.Ready.HasValue && p.Ready.Value);
            var numberRequested = functionDeploymentInformation == null ? 0 : functionDeploymentInformation.Pods.Count();
            contextResponse.StatusCode = 200;
            contextResponse.WriteAsJsonAsync(new FunctionStatus(numberReady, numberRequested), FunctionStatusSerializerContext.Default.FunctionStatus);
        }
        else
        {
            contextResponse.StatusCode = 404;
        }
    }

    private static void BuildWakeResponse(HistoryHttpMemoryService historyHttpService, IReplicasService replicasService,
        string functionName, HttpResponse contextResponse)
    {
        var function = SearchFunction(replicasService, functionName);
        if (function != null)
        {
            historyHttpService.SetTickLastCall(functionName, DateTime.Now.Ticks);
            contextResponse.StatusCode = 204;
        }
        else
        {
            contextResponse.StatusCode = 404;
        }
    }

    private static DeploymentInformation? SearchFunction(IReplicasService replicasService, string functionName)
    {
        var function = replicasService.Deployments.Functions.FirstOrDefault(f => f.Deployment == functionName);
        return function;
    }

    private async Task BuildAsyncResponseAsync(IReplicasService replicasService, string functionName, CustomRequest customRequest, HttpResponse contextResponse)
    {
        var function = SearchFunction(replicasService, functionName);
        if (function == null)
        {
            contextResponse.StatusCode = 404;
            return;
        }

        var dataString = SlimfaasSerializer.Serialize(customRequest);
        await slimFaasQueue.EnqueueAsync(functionName, dataString);

        contextResponse.StatusCode = 202;
    }

    private async Task BuildSyncResponseAsync(HttpContext context, HistoryHttpMemoryService historyHttpService,
        ISendClient sendClient, IReplicasService replicasService, string functionName, string functionPath)
    {
        var function = SearchFunction(replicasService, functionName);
        if (function == null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        await WaitForAnyPodStartedAsync(context, historyHttpService, replicasService, functionName);

        var responseMessagePromise = sendClient.SendHttpRequestSync(context, functionName, functionPath, context.Request.QueryString.ToUriComponent());

        var lastSetTicks = DateTime.Now.Ticks;
        historyHttpService.SetTickLastCall(functionName, lastSetTicks);
        while (!responseMessagePromise.IsCompleted)
        {
            await Task.Delay(10, context.RequestAborted);
            var isOneSecondElapsed = new DateTime(lastSetTicks) < DateTime.Now.AddSeconds(-1);
            if (!isOneSecondElapsed) continue;
            lastSetTicks = DateTime.Now.Ticks;
            historyHttpService.SetTickLastCall(functionName, lastSetTicks);
        }

        historyHttpService.SetTickLastCall(functionName, DateTime.Now.Ticks);
        using var responseMessage = responseMessagePromise.Result;
        context.Response.StatusCode = (int)responseMessage.StatusCode;
        CopyFromTargetResponseHeaders(context, responseMessage);
        await responseMessage.Content.CopyToAsync(context.Response.Body);
    }

    private async Task WaitForAnyPodStartedAsync(HttpContext context, HistoryHttpMemoryService historyHttpService,
        IReplicasService replicasService, string functionName)
    {
        var numberLoop = _timeoutMaximumWaitWakeSyncFunctionMilliSecond / 10;
        var lastSetTicks = DateTime.Now.Ticks;
        historyHttpService.SetTickLastCall(functionName, lastSetTicks);
        while (numberLoop > 0)
        {
            var isAnyContainerStarted = replicasService.Deployments.Functions.Any(f =>
                f is { Replicas: > 0, Pods: not null } && f.Pods.Any(p => p.Ready.HasValue && p.Ready.Value));
            if (!isAnyContainerStarted && !context.RequestAborted.IsCancellationRequested)
            {
                numberLoop--;
                await Task.Delay(10, context.RequestAborted);
                var isOneSecondElapsed = new DateTime(lastSetTicks) < DateTime.Now.AddSeconds(-1);
                if (isOneSecondElapsed)
                {
                    lastSetTicks = DateTime.Now.Ticks;
                    historyHttpService.SetTickLastCall(functionName, lastSetTicks);
                }
                continue;
            }

            numberLoop = 0;
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
    private const string StatusFunction = "/status-function";
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
        faasLogger.LogDebug("{Method}: {Function}{UriComponent}", requestMethod, pathString,
            requestQueryString.ToUriComponent());

        var functionType = functionBeginPath switch
        {
            AsyncFunction => FunctionType.Async,
            Function => FunctionType.Sync,
            StatusFunction => FunctionType.Status,
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
        }else if (path.StartsWithSegments(StatusFunction)) {
            functionBeginPath = $"{StatusFunction}";
        }

        return functionBeginPath;
    }


}
