using System.Text.Json.Serialization;
using MemoryPack;
using SlimFaas.Database;
using SlimFaas.Kubernetes;

namespace SlimFaas;

public enum FunctionType
{
    Sync,
    Async,
    Wake,
    Status,
    Publish,
    NotAFunction
}

public record FunctionStatus(int NumberReady, int NumberRequested);

[JsonSerializable(typeof(FunctionStatus))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class FunctionStatusSerializerContext : JsonSerializerContext
{
}

public class SlimProxyMiddleware(RequestDelegate next, ISlimFaasQueue slimFaasQueue, IWakeUpFunction wakeUpFunction,
    ILogger<SlimProxyMiddleware> logger,
    int timeoutWaitWakeSyncFunctionMilliSecond =
        EnvironmentVariables.SlimProxyMiddlewareTimeoutWaitWakeSyncFunctionMilliSecondsDefault,
    string slimFaasSubscribeEventsDefault = EnvironmentVariables.SlimFaasSubscribeEventsDefault)

{
    private const string AsyncFunction = "/async-function";
    private const string StatusFunction = "/status-function";
    private const string WakeFunction = "/wake-function";
    private const string Function = "/function";
    private const string PublishEvent = "/publish-event";

    private readonly int[] _slimFaasPorts = EnvironmentVariables.ReadIntegers(EnvironmentVariables.SlimFaasPorts,
        EnvironmentVariables.SlimFaasPortsDefault);

    private readonly int _timeoutMaximumWaitWakeSyncFunctionMilliSecond = EnvironmentVariables.ReadInteger(logger,
        EnvironmentVariables.TimeMaximumWaitForAtLeastOnePodStartedForSyncFunction,
        timeoutWaitWakeSyncFunctionMilliSecond);

    private readonly IDictionary<string, IList<string>> _slimFaasSubscribeEvents = EnvironmentVariables.ReadSlimFaasSubscribeEvents(logger,
        EnvironmentVariables.SlimFaasSubscribeEvents,
        slimFaasSubscribeEventsDefault);

    public async Task InvokeAsync(HttpContext context,
        HistoryHttpMemoryService historyHttpService, ISendClient sendClient, IReplicasService replicasService)
    {

        if (!HostPort.IsSamePort(context.Request.Host.Port, _slimFaasPorts))
        {
            await next(context);
            return;
        }

        HttpRequest contextRequest = context.Request;
        (string functionPath, string functionName, FunctionType functionType) = GetFunctionInfo(logger, contextRequest);
        HttpResponse contextResponse = context.Response;

        switch (functionType)
        {
            case FunctionType.NotAFunction:
                await next(context);
                return;
            case FunctionType.Wake:
                BuildWakeResponse(replicasService, wakeUpFunction, functionName, contextResponse);
                return;
            case FunctionType.Status:
                BuildStatusResponse(replicasService, functionName, contextResponse);
                return;
            case FunctionType.Sync:
                await BuildSyncResponseAsync(context, historyHttpService, sendClient, replicasService, functionName,
                    functionPath);
                return;
            case FunctionType.Publish:
                await BuildPublishResponseAsync(context, historyHttpService, sendClient, replicasService, functionName,
                    functionPath);
                return;
            case FunctionType.Async:
            default:
                {
                    await BuildAsyncResponseAsync(context, replicasService, functionName, functionPath);
                    break;
                }
        }
    }

    private static Boolean MessageComeFromNamepaceInternal(HttpContext context, IReplicasService replicasService)
    {
        IList<string> podIps = replicasService.Deployments.Pods.Select(p => p.Ip).ToList();
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var remoteIp = context.Connection.RemoteIpAddress?.ToString();

        if (IsInternalIp(forwardedFor, podIps) || IsInternalIp(remoteIp, podIps))
        {
            return true;
        }

        return false;
    }

    private static bool IsInternalIp(string? ipAddress, IList<string> podIps)
    {
        return ipAddress != null && podIps.Contains(ipAddress);
    }

    private static void BuildStatusResponse(IReplicasService replicasService,
        string functionName, HttpResponse contextResponse)
    {
        DeploymentInformation? function = SearchFunction(replicasService, functionName);
        if (function != null)
        {
            DeploymentInformation? functionDeploymentInformation =
                replicasService.Deployments.Functions.FirstOrDefault(f => f.Deployment == functionName);
            int numberReady = functionDeploymentInformation == null
                ? 0
                : functionDeploymentInformation.Pods.Count(p => p.Ready.HasValue && p.Ready.Value);
            int numberRequested =
                functionDeploymentInformation?.Replicas ?? 0;
            contextResponse.StatusCode = 200;
            contextResponse.WriteAsJsonAsync(new FunctionStatus(numberReady, numberRequested),
                FunctionStatusSerializerContext.Default.FunctionStatus);
        }
        else
        {
            contextResponse.StatusCode = 404;
        }
    }

    private static void BuildWakeResponse(IReplicasService replicasService, IWakeUpFunction wakeUpFunction,
        string functionName, HttpResponse contextResponse)
    {
        DeploymentInformation? function = SearchFunction(replicasService, functionName);
        if (function != null)
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            wakeUpFunction.FireAndForgetWakeUpAsync(functionName);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            contextResponse.StatusCode = 204;
        }
        else
        {
            contextResponse.StatusCode = 404;
        }
    }

    private static List<DeploymentInformation> SearchFunctions(HttpContext context, IReplicasService replicasService, string eventName)
    {
        // example: "Public:my-event-name1,Private:my-event-name2,my-event-name3"
        var result = new List<DeploymentInformation>();
        foreach (DeploymentInformation deploymentInformation in replicasService.Deployments.Functions)
        {
            if(deploymentInformation.SubscribeEvents == null)
            {
                continue;
            }
            foreach (string deploymentInformationSubscribeEvent in deploymentInformation.SubscribeEvents)
            {
                var splits = deploymentInformationSubscribeEvent.Split(":");
                if (splits.Length == 1 && splits[0] == eventName)
                {
                    if (deploymentInformation.Visibility == FunctionVisibility.Public ||
                        MessageComeFromNamepaceInternal(context, replicasService))
                    {
                        result.Add(deploymentInformation);
                    }
                }
                else if (splits.Length == 2 && splits[1] == eventName)
                {
                    var visibility = splits[0];
                    var visibilityEnum = Enum.Parse<FunctionVisibility>(visibility, true);
                    if(visibilityEnum == FunctionVisibility.Private && MessageComeFromNamepaceInternal(context, replicasService))
                    {
                        result.Add(deploymentInformation);
                    } else if(visibilityEnum == FunctionVisibility.Public)
                    {
                        result.Add(deploymentInformation);
                    }
                }

            }

        }
        return result;
    }

    private static DeploymentInformation? SearchFunction(IReplicasService replicasService, string functionName)
    {
        DeploymentInformation? function =
            replicasService.Deployments.Functions.FirstOrDefault(f => f.Deployment == functionName);
        return function;
    }

    private static FunctionVisibility GetFunctionVisibility(ILogger<SlimProxyMiddleware> logger, DeploymentInformation function, string path)
    {
        if (function.PathsStartWithVisibility?.Count > 0)
        {
            foreach (string pathStartWith in function.PathsStartWithVisibility)
            {
                var splits = pathStartWith.Split(":");
                if (splits.Length == 2 && path.ToLowerInvariant().StartsWith(splits[1].ToLowerInvariant()))
                {
                    var visibility = splits[0];
                    var visibilityEnum = Enum.Parse<FunctionVisibility>(visibility, true);
                    return visibilityEnum;
                }

                logger.LogWarning("PathStartWithVisibility {PathStartWith} should be prefixed by Public: or Private:", pathStartWith);
            }
        }
        return function.Visibility;
    }

    private async Task BuildAsyncResponseAsync(HttpContext context, IReplicasService replicasService, string functionName,
        string functionPath)
    {
        DeploymentInformation? function = SearchFunction(replicasService, functionName);
        if (function == null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        var visibility = GetFunctionVisibility(logger, function, functionPath);

        if (visibility == FunctionVisibility.Private && !MessageComeFromNamepaceInternal(context, replicasService))
        {
            context.Response.StatusCode = 404;
            return;
        }
        CustomRequest customRequest =
            await InitCustomRequest(context, context.Request, functionName, functionPath);

        var bin = MemoryPackSerializer.Serialize(customRequest);
        await slimFaasQueue.EnqueueAsync(functionName, bin);

        context.Response.StatusCode = 202;
    }

    private async Task BuildPublishResponseAsync(HttpContext context, HistoryHttpMemoryService historyHttpService,
        ISendClient sendClient, IReplicasService replicasService, string eventName, string functionPath)
    {
        logger.LogDebug("Receiving event: {EventName}", eventName);
        var functions = SearchFunctions(context, replicasService, eventName);
        var slimFaasSubscribeEvents = _slimFaasSubscribeEvents.Where(s => s.Key == eventName);
        if (functions.Count <= 0 && !slimFaasSubscribeEvents.Any())
        {
            context.Response.StatusCode = 404;
            return;
        }
        var lastSetTicks = DateTime.UtcNow.Ticks;

        List<Task<HttpResponseMessage>> tasks = new();
        foreach (DeploymentInformation function in functions)
        {
            historyHttpService.SetTickLastCall(function.Deployment, lastSetTicks);
            foreach (var pod in function.Pods)
            {
                if (pod.Ready != true)
                {
                    continue;
                }

                string baseFunctionPodUrl =
                    Environment.GetEnvironmentVariable(EnvironmentVariables.BaseFunctionPodUrl) ??
                    EnvironmentVariables.BaseFunctionPodUrlDefault;

                var baseUrl = SlimDataEndpoint.Get(pod, baseFunctionPodUrl);
                logger.LogDebug("Sending event {EventName} to {FunctionDeployment} at {BaseUrl} with path {FunctionPath} and query {UriComponent}", eventName, function.Deployment, baseUrl, functionPath, context.Request.QueryString.ToUriComponent());
                Task<HttpResponseMessage> responseMessagePromise = sendClient.SendHttpRequestSync(context, function.Deployment,
                    functionPath, context.Request.QueryString.ToUriComponent(), baseUrl);
                tasks.Add(responseMessagePromise);
            }
        }

        foreach (KeyValuePair<string,IList<string>> slimFaasSubscribeEvent in slimFaasSubscribeEvents)
        {
            foreach (string baseUrl in slimFaasSubscribeEvent.Value)
            {
                logger.LogDebug("Sending event {EventName} to {BaseUrl} with path {FunctionPath} and query {UriComponent}", eventName, baseUrl, functionPath, context.Request.QueryString.ToUriComponent());
                Task<HttpResponseMessage> responseMessagePromise = sendClient.SendHttpRequestSync(context, "",
                    functionPath, context.Request.QueryString.ToUriComponent(), baseUrl);
                tasks.Add(responseMessagePromise);
            }
        }

        while (tasks.Any(t => !t.IsCompleted) && !context.RequestAborted.IsCancellationRequested)
        {
            await Task.Delay(10, context.RequestAborted);
            bool isOneSecondElapsed = new DateTime(lastSetTicks, DateTimeKind.Utc) < DateTime.UtcNow.AddSeconds(-1);
            if (!isOneSecondElapsed)
            {
                continue;
            }

            lastSetTicks = DateTime.UtcNow.Ticks;
            foreach (DeploymentInformation function in functions)
            {
                historyHttpService.SetTickLastCall(function.Deployment, lastSetTicks);
            }
        }

        context.Response.StatusCode = 204;
    }

    private async Task BuildSyncResponseAsync(HttpContext context, HistoryHttpMemoryService historyHttpService,
        ISendClient sendClient, IReplicasService replicasService, string functionName, string functionPath)
    {
        DeploymentInformation? function = SearchFunction(replicasService, functionName);
        if (function == null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        var visibility = GetFunctionVisibility(logger, function, functionPath);

        if (visibility == FunctionVisibility.Private && !MessageComeFromNamepaceInternal(context, replicasService))
        {
            context.Response.StatusCode = 404;
            return;
        }

        await WaitForAnyPodStartedAsync(context, historyHttpService, replicasService, functionName);

        Task<HttpResponseMessage> responseMessagePromise = sendClient.SendHttpRequestSync(context, functionName,
            functionPath, context.Request.QueryString.ToUriComponent());

        long lastSetTicks = DateTime.UtcNow.Ticks;
        historyHttpService.SetTickLastCall(functionName, lastSetTicks);
        while (!responseMessagePromise.IsCompleted)
        {
            await Task.Delay(10, context.RequestAborted);
            bool isOneSecondElapsed = new DateTime(lastSetTicks, DateTimeKind.Utc) < DateTime.UtcNow.AddSeconds(-1);
            if (!isOneSecondElapsed)
            {
                continue;
            }

            lastSetTicks = DateTime.UtcNow.Ticks;
            historyHttpService.SetTickLastCall(functionName, lastSetTicks);
        }

        historyHttpService.SetTickLastCall(functionName, DateTime.UtcNow.Ticks);
        using HttpResponseMessage responseMessage = responseMessagePromise.Result;
        context.Response.StatusCode = (int)responseMessage.StatusCode;
        CopyFromTargetResponseHeaders(context, responseMessage);
        await responseMessage.Content.CopyToAsync(context.Response.Body);
    }

    private async Task WaitForAnyPodStartedAsync(HttpContext context, HistoryHttpMemoryService historyHttpService,
        IReplicasService replicasService, string functionName)
    {
        int numberLoop = _timeoutMaximumWaitWakeSyncFunctionMilliSecond / 10;
        long lastSetTicks = DateTime.UtcNow.Ticks;
        historyHttpService.SetTickLastCall(functionName, lastSetTicks);
        while (numberLoop > 0)
        {
            bool isAnyContainerStarted = replicasService.Deployments.Functions.Any(f =>
                f is { Replicas: > 0, Pods: not null } && f.Pods.Any(p => p.Ready.HasValue && p.Ready.Value));
            if (!isAnyContainerStarted && !context.RequestAborted.IsCancellationRequested)
            {
                numberLoop--;
                await Task.Delay(10, context.RequestAborted);
                bool isOneSecondElapsed = new DateTime(lastSetTicks) < DateTime.UtcNow.AddSeconds(-1);
                if (isOneSecondElapsed)
                {
                    lastSetTicks = DateTime.UtcNow.Ticks;
                    historyHttpService.SetTickLastCall(functionName, lastSetTicks);
                }

                continue;
            }

            numberLoop = 0;
        }
    }

    private void CopyFromTargetResponseHeaders(HttpContext context, HttpResponseMessage responseMessage)
    {
        foreach (KeyValuePair<string, IEnumerable<string>> header in responseMessage.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (KeyValuePair<string, IEnumerable<string>> header in responseMessage.Content.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        context.Response.Headers.Remove("transfer-encoding");
    }

    private static async Task<CustomRequest> InitCustomRequest(HttpContext context, HttpRequest contextRequest,
        string functionName, string functionPath)
    {
        List<CustomHeader> customHeaders = contextRequest.Headers
            .Select(headers => new CustomHeader(headers.Key, headers.Value.ToArray())).ToList();

        string requestMethod = contextRequest.Method;
        byte[]? requestBodyBytes = null;
        if (!HttpMethods.IsGet(requestMethod) &&
            !HttpMethods.IsHead(requestMethod) &&
            !HttpMethods.IsDelete(requestMethod) &&
            !HttpMethods.IsTrace(requestMethod))
        {
            using StreamContent streamContent = new StreamContent(context.Request.Body);
            using MemoryStream memoryStream = new MemoryStream();
            await streamContent.CopyToAsync(memoryStream);
            requestBodyBytes = memoryStream.ToArray();
        }

        QueryString requestQueryString = contextRequest.QueryString;
        CustomRequest customRequest = new CustomRequest
        {
            Headers = customHeaders,
            FunctionName = functionName,
            Path = functionPath,
            Body = requestBodyBytes,
            Query = requestQueryString.ToUriComponent(),
            Method = requestMethod
        };
        return customRequest;
    }

    private static FunctionInfo GetFunctionInfo(ILogger<SlimProxyMiddleware> faasLogger, HttpRequest contextRequest)
    {
        string requestMethod = contextRequest.Method;
        PathString requestPath = contextRequest.Path;
        QueryString requestQueryString = contextRequest.QueryString;
        string functionBeginPath = FunctionBeginPath(requestPath);
        if (string.IsNullOrEmpty(functionBeginPath))
        {
            return new FunctionInfo(string.Empty, string.Empty);
        }

        string pathString = requestPath.ToUriComponent();
        string[] paths = pathString.Split("/");
        if (paths.Length <= 2)
        {
            return new FunctionInfo(string.Empty, string.Empty);
        }

        string functionName = paths[2];
        string functionPath = pathString.Replace($"{functionBeginPath}/{functionName}", "");
        faasLogger.LogDebug("{Method}: {Function}{UriComponent}", requestMethod, pathString,
            requestQueryString.ToUriComponent());

        FunctionType functionType = functionBeginPath switch
        {
            AsyncFunction => FunctionType.Async,
            Function => FunctionType.Sync,
            StatusFunction => FunctionType.Status,
            WakeFunction => FunctionType.Wake,
            PublishEvent => FunctionType.Publish,
            _ => FunctionType.NotAFunction
        };
        return new FunctionInfo(functionPath, functionName, functionType);
    }

    private static string FunctionBeginPath(PathString path)
    {
        string functionBeginPath = string.Empty;
        if (path.StartsWithSegments(AsyncFunction))
        {
            functionBeginPath = $"{AsyncFunction}";
        }
        else if (path.StartsWithSegments(Function))
        {
            functionBeginPath = $"{Function}";
        }
        else if (path.StartsWithSegments(WakeFunction))
        {
            functionBeginPath = $"{WakeFunction}";
        }
        else if (path.StartsWithSegments(StatusFunction))
        {
            functionBeginPath = $"{StatusFunction}";
        }
        else if (path.StartsWithSegments(PublishEvent))
        {
            functionBeginPath = $"{PublishEvent}";
        }

        return functionBeginPath;
    }

    private record FunctionInfo(string FunctionPath, string FunctionName,
        FunctionType FunctionType = FunctionType.NotAFunction);
}
