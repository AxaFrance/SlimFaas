
using System.Text.Json;

namespace SlimFaas;


record struct RequestToWait(Task<HttpResponseMessage> Task, CustomRequest CustomRequest);

public class SlimWorker : BackgroundService
{
    private readonly HistoryHttpMemoryService _historyHttpService;
    private readonly ILogger<SlimWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly int _delay;
    private readonly IQueue _queue;
    private readonly IReplicasService _replicasService;

    public SlimWorker(IQueue queue, IReplicasService replicasService, HistoryHttpMemoryService historyHttpService, ILogger<SlimWorker> logger, IServiceProvider serviceProvider, int delay = 10)
    {
        _historyHttpService = historyHttpService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _delay = int.Parse(Environment.GetEnvironmentVariable("SLIM_WORKER_DELAY_MILLISECONDS")  ?? delay.ToString());
        _queue = queue;
        _replicasService = replicasService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var processingTasks = new Dictionary<string, IList<RequestToWait>>();
        var setTickLastCallCounterDictionary = new Dictionary<string, int>();
        while (stoppingToken.IsCancellationRequested == false)
        {
            await DoOneCycle(stoppingToken, setTickLastCallCounterDictionary, processingTasks);
        }
    }

    private async Task DoOneCycle(CancellationToken stoppingToken, Dictionary<string, int> setTickLastCallCounterDictionary,
        Dictionary<string, IList<RequestToWait>> processingTasks)
    {
        try
        {
            await Task.Delay(_delay, stoppingToken);
            var deployments = _replicasService.Deployments;
            var functions = deployments.Functions;
            var slimFaas = deployments.SlimFaas;
            foreach (var function in functions)
            {
                var functionDeployment = function.Deployment;
                setTickLastCallCounterDictionary.TryAdd(functionDeployment, 0);
                var numberProcessingTasks = ManageProcessingTasks(processingTasks, functionDeployment);
                var numberLimitProcessingTasks = ComputeNumberLimitProcessingTasks(slimFaas, function);
                setTickLastCallCounterDictionary[functionDeployment]++;
                var functionReplicas = function.Replicas;
                await UpdateTickLastCallIfRequestStillInProgress(functionReplicas, setTickLastCallCounterDictionary,
                    functionDeployment, numberProcessingTasks);
                if (functionReplicas == 0) continue;
                var isAnyContainerStarted = function.Pods?.Any(p => p.Ready.HasValue && p.Ready.Value);
                if(!isAnyContainerStarted.HasValue || !isAnyContainerStarted.Value) continue;
                if (numberProcessingTasks >= numberLimitProcessingTasks) continue;
                await SendHttpRequestToFunction(processingTasks, numberLimitProcessingTasks, numberProcessingTasks,
                    functionDeployment);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Global Error in SlimFaas Worker");
        }
    }

    private async Task SendHttpRequestToFunction(Dictionary<string, IList<RequestToWait>> processingTasks, int? numberLimitProcessingTasks, int numberProcessingTasks,
        string functionDeployment)
    {
        var numberTasksToDequeue = numberLimitProcessingTasks - numberProcessingTasks;
        var jsons = await _queue.DequeueAsync(functionDeployment,
            numberTasksToDequeue.HasValue ? (long)numberTasksToDequeue : 1);
        foreach (var requestJson in jsons)
        {
            var customRequest =
                JsonSerializer.Deserialize(requestJson, CustomRequestSerializerContext.Default.CustomRequest);
            _logger.LogDebug("{CustomRequestMethod}: {CustomRequestPath}{CustomRequestQuery} Sending",
                customRequest.Method, customRequest.Path, customRequest.Query);
            _logger.LogDebug("{RequestJson}", requestJson);
            _historyHttpService.SetTickLastCall(functionDeployment, DateTime.Now.Ticks);
            using var scope = _serviceProvider.CreateScope();
            var taskResponse = scope.ServiceProvider.GetRequiredService<ISendClient>()
                .SendHttpRequestAsync(customRequest);
            processingTasks[functionDeployment].Add(new RequestToWait(taskResponse, customRequest));
        }
    }

    private async Task UpdateTickLastCallIfRequestStillInProgress(int? functionReplicas,
        Dictionary<string, int> setTickLastCallCounterDictionnary, string functionDeployment, int numberProcessingTasks)
    {
        var counterLimit = functionReplicas == 0 ? 10 : 300;

        if (setTickLastCallCounterDictionnary[functionDeployment] > counterLimit)
        {
            setTickLastCallCounterDictionnary[functionDeployment] = 0;
            var queueLenght = await _queue.CountAsync(functionDeployment);
            if (queueLenght > 0 || numberProcessingTasks > 0)
            {
                _historyHttpService.SetTickLastCall(functionDeployment, DateTime.Now.Ticks);
            }
        }
    }

    private static int? ComputeNumberLimitProcessingTasks(SlimFaasDeploymentInformation slimFaas,
        DeploymentInformation function)
    {
        int? numberLimitProcessingTasks;
        var numberReplicas = slimFaas.Replicas ?? 0;

        if (function.NumberParallelRequest < numberReplicas || numberReplicas == 0)
        {
            numberLimitProcessingTasks = 1;
        }
        else
        {
            numberLimitProcessingTasks = function.NumberParallelRequest / slimFaas.Replicas;
        }

        return numberLimitProcessingTasks;
    }

    private int ManageProcessingTasks(Dictionary<string, IList<RequestToWait>> processingTasks, string functionDeployment)
    {
        if (processingTasks.ContainsKey(functionDeployment) == false)
        {
            processingTasks.Add(functionDeployment, new List<RequestToWait>());
        }

        var httpResponseMessagesToDelete = new List<RequestToWait>();
        foreach (var processing in processingTasks[functionDeployment])
        {
            try
            {
                if (!processing.Task.IsCompleted) continue;
                var httpResponseMessage = processing.Task.Result;
                httpResponseMessage.Dispose();
                _logger.LogDebug(
                    "{CustomRequestMethod}: /async-function/{CustomRequestPath}{CustomRequestQuery} {StatusCode}",
                    processing.CustomRequest.Method, processing.CustomRequest.Path, processing.CustomRequest.Query,
                    httpResponseMessage.StatusCode);
                httpResponseMessagesToDelete.Add(processing);
                _historyHttpService.SetTickLastCall(functionDeployment, DateTime.Now.Ticks);
            }
            catch (Exception e)
            {
                httpResponseMessagesToDelete.Add(processing);
                _logger.LogWarning("Request Error: {Message} {StackTrace}", e.Message, e.StackTrace);
                _historyHttpService.SetTickLastCall(functionDeployment, DateTime.Now.Ticks);
            }
        }

        foreach (var httpResponseMessage in httpResponseMessagesToDelete)
        {
            processingTasks[functionDeployment].Remove(httpResponseMessage);
        }
        var numberProcessingTasks = processingTasks[functionDeployment].Count;
        return numberProcessingTasks;
    }
}
