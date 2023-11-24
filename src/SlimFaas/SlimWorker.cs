
using SlimFaas.Kubernetes;

namespace SlimFaas;

record struct RequestToWait(Task<HttpResponseMessage> Task, CustomRequest CustomRequest);

public class SlimWorker(ISlimFaasQueue slimFaasQueue, IReplicasService replicasService,
        HistoryHttpMemoryService historyHttpService, ILogger<SlimWorker> logger, IServiceProvider serviceProvider, SlimDataStatus slimDataStatus,
        int delay = EnvironmentVariables.SlimWorkerDelayMillisecondsDefault)
    : BackgroundService
{
    private readonly int _delay = EnvironmentVariables.ReadInteger<SlimWorker>(logger, EnvironmentVariables.SlimWorkerDelayMilliseconds, delay);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await slimDataStatus.WaitForReadyAsync();
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
            var deployments = replicasService.Deployments;
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
                var queueLenght = await UpdateTickLastCallIfRequestStillInProgress(functionReplicas, setTickLastCallCounterDictionary,
                    functionDeployment, numberProcessingTasks);
                if (functionReplicas == 0 || queueLenght <= 0 ) continue;
                var isAnyContainerStarted = function.Pods?.Any(p => p.Ready.HasValue && p.Ready.Value);
                if(!isAnyContainerStarted.HasValue || !isAnyContainerStarted.Value) continue;
                if (numberProcessingTasks >= numberLimitProcessingTasks) continue;
                await SendHttpRequestToFunction(processingTasks, numberLimitProcessingTasks, numberProcessingTasks,
                    functionDeployment);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Global Error in SlimFaas Worker");
        }
    }

    private async Task SendHttpRequestToFunction(Dictionary<string, IList<RequestToWait>> processingTasks, int? numberLimitProcessingTasks, int numberProcessingTasks,
        string functionDeployment)
    {
        var numberTasksToDequeue = numberLimitProcessingTasks - numberProcessingTasks;
        var jsons = await slimFaasQueue.DequeueAsync(functionDeployment,
            numberTasksToDequeue.HasValue ? (long)numberTasksToDequeue : 1);
        foreach (var requestJson in jsons)
        {
            var customRequest = SlimfaasSerializer.Deserialize(requestJson);
            logger.LogDebug("{CustomRequestMethod}: {CustomRequestPath}{CustomRequestQuery} Sending",
                customRequest.Method, customRequest.Path, customRequest.Query);
            logger.LogDebug("{RequestJson}", requestJson);
            historyHttpService.SetTickLastCall(functionDeployment, DateTime.Now.Ticks);
            using var scope = serviceProvider.CreateScope();
            var taskResponse = scope.ServiceProvider.GetRequiredService<ISendClient>()
                .SendHttpRequestAsync(customRequest);
            processingTasks[functionDeployment].Add(new RequestToWait(taskResponse, customRequest));
        }
    }

    private async Task<long> UpdateTickLastCallIfRequestStillInProgress(int? functionReplicas,
        Dictionary<string, int> setTickLastCallCounterDictionnary, string functionDeployment, int numberProcessingTasks)
    {
        var counterLimit = functionReplicas == 0 ? 10 : 300;
        var queueLenght = await slimFaasQueue.CountAsync(functionDeployment);
        if (setTickLastCallCounterDictionnary[functionDeployment] > counterLimit)
        {
            setTickLastCallCounterDictionnary[functionDeployment] = 0;

            if (queueLenght > 0 || numberProcessingTasks > 0)
            {
                historyHttpService.SetTickLastCall(functionDeployment, DateTime.Now.Ticks);
            }
        }
        return queueLenght;
    }

    private static int? ComputeNumberLimitProcessingTasks(SlimFaasDeploymentInformation slimFaas,
        DeploymentInformation function)
    {
        int? numberLimitProcessingTasks;
        var numberReplicas = slimFaas.Replicas;

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
                logger.LogDebug(
                    "{CustomRequestMethod}: /async-function/{CustomRequestPath}{CustomRequestQuery} {StatusCode}",
                    processing.CustomRequest.Method, processing.CustomRequest.Path, processing.CustomRequest.Query,
                    httpResponseMessage.StatusCode);
                httpResponseMessagesToDelete.Add(processing);
                historyHttpService.SetTickLastCall(functionDeployment, DateTime.Now.Ticks);
            }
            catch (Exception e)
            {
                httpResponseMessagesToDelete.Add(processing);
                logger.LogWarning("Request Error: {Message} {StackTrace}", e.Message, e.StackTrace);
                historyHttpService.SetTickLastCall(functionDeployment, DateTime.Now.Ticks);
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
