using MemoryPack;
using SlimData;
using SlimFaas.Database;
using SlimFaas.Kubernetes;

namespace SlimFaas;

internal record struct RequestToWait(Task<HttpResponseMessage> Task, CustomRequest CustomRequest, string id);

public class SlimWorker(ISlimFaasQueue slimFaasQueue, IReplicasService replicasService,
        HistoryHttpMemoryService historyHttpService, ILogger<SlimWorker> logger, IServiceProvider serviceProvider,
        ISlimDataStatus slimDataStatus,
        int delay = EnvironmentVariables.SlimWorkerDelayMillisecondsDefault)
    : BackgroundService
{
    private readonly int _delay =
        EnvironmentVariables.ReadInteger(logger, EnvironmentVariables.SlimWorkerDelayMilliseconds, delay);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await slimDataStatus.WaitForReadyAsync();
        Dictionary<string, IList<RequestToWait>> processingTasks = new();
        Dictionary<string, int> setTickLastCallCounterDictionary = new();
        while (stoppingToken.IsCancellationRequested == false)
        {
            await DoOneCycle(stoppingToken, setTickLastCallCounterDictionary, processingTasks);
        }
    }

    private async Task DoOneCycle(CancellationToken stoppingToken,
        Dictionary<string, int> setTickLastCallCounterDictionary,
        Dictionary<string, IList<RequestToWait>> processingTasks)
    {
        try
        {
            await Task.Delay(_delay, stoppingToken);
            DeploymentsInformations deployments = replicasService.Deployments;
            IList<DeploymentInformation> functions = deployments.Functions;
            SlimFaasDeploymentInformation slimFaas = deployments.SlimFaas;
            foreach (DeploymentInformation function in functions)
            {
                string functionDeployment = function.Deployment;
                setTickLastCallCounterDictionary.TryAdd(functionDeployment, 0);
                int numberProcessingTasks = await ManageProcessingTasksAsync(slimFaasQueue, processingTasks, functionDeployment);
                int? numberLimitProcessingTasks = ComputeNumberLimitProcessingTasks(slimFaas, function);
                setTickLastCallCounterDictionary[functionDeployment]++;
                int functionReplicas = function.Replicas;
                long queueLength = await UpdateTickLastCallIfRequestStillInProgress(functionReplicas,
                    setTickLastCallCounterDictionary,
                    functionDeployment, numberProcessingTasks);
                if (functionReplicas == 0 || queueLength <= 0)
                {
                    continue;
                }

                bool? isAnyContainerStarted = function.Pods?.Any(p => p.Ready.HasValue && p.Ready.Value);
                if (!isAnyContainerStarted.HasValue || !isAnyContainerStarted.Value)
                {
                    continue;
                }

                if (numberProcessingTasks >= numberLimitProcessingTasks)
                {
                    continue;
                }

                await SendHttpRequestToFunction(processingTasks, numberLimitProcessingTasks, numberProcessingTasks,
                    functionDeployment);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Global Error in SlimFaas Worker");
        }
    }

    private async Task SendHttpRequestToFunction(Dictionary<string, IList<RequestToWait>> processingTasks,
        int? numberLimitProcessingTasks, int numberProcessingTasks,
        string functionDeployment)
    {
        int? numberTasksToDequeue = numberLimitProcessingTasks - numberProcessingTasks;
        IList<QueueData>? jsons = await slimFaasQueue.DequeueAsync(functionDeployment,
            numberTasksToDequeue.HasValue ? (long)numberTasksToDequeue : 1);
        if (jsons == null)
        {
            return;
        }
        foreach (var requestJson in jsons)
        {
            CustomRequest customRequest = MemoryPackSerializer.Deserialize<CustomRequest>(requestJson.Data);

            logger.LogDebug("{CustomRequestMethod}: {CustomRequestPath}{CustomRequestQuery} Sending",
                customRequest.Method, customRequest.Path, customRequest.Query);
            logger.LogDebug("{RequestJson}", requestJson);
            historyHttpService.SetTickLastCall(functionDeployment, DateTime.UtcNow.Ticks);
            using IServiceScope scope = serviceProvider.CreateScope();
            Task<HttpResponseMessage> taskResponse = scope.ServiceProvider.GetRequiredService<ISendClient>()
                .SendHttpRequestAsync(customRequest);
            processingTasks[functionDeployment].Add(new RequestToWait(taskResponse, customRequest, requestJson.Id));
        }
    }

    private async Task<long> UpdateTickLastCallIfRequestStillInProgress(int? functionReplicas,
        Dictionary<string, int> setTickLastCallCounterDictionnary, string functionDeployment, int numberProcessingTasks)
    {
        //int counterLimit = functionReplicas == 0 ? 10 : 300;
        long queueLength = await slimFaasQueue.CountAsync(functionDeployment);
        //if (setTickLastCallCounterDictionnary[functionDeployment] > counterLimit)
        {
            //setTickLastCallCounterDictionnary[functionDeployment] = 0;

            if (queueLength > 0 || numberProcessingTasks > 0)
            {
                historyHttpService.SetTickLastCall(functionDeployment, DateTime.UtcNow.Ticks);
            }
        }

        return queueLength;
    }

    private static int? ComputeNumberLimitProcessingTasks(SlimFaasDeploymentInformation slimFaas,
        DeploymentInformation function)
    {
        int? numberLimitProcessingTasks;
        int numberReplicas = slimFaas.Replicas;

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

    private async Task<int> ManageProcessingTasksAsync(ISlimFaasQueue slimFaasQueue, Dictionary<string, IList<RequestToWait>> processingTasks,
        string functionDeployment)
    {
        if (processingTasks.ContainsKey(functionDeployment) == false)
        {
            processingTasks.Add(functionDeployment, new List<RequestToWait>());
        }
        var listQueueItemStatus = new ListQueueItemStatus();
        var queueItemStatusList = new List<QueueItemStatus>();
        listQueueItemStatus.Items = queueItemStatusList;
        List<RequestToWait> httpResponseMessagesToDelete = new();
        foreach (RequestToWait processing in processingTasks[functionDeployment])
        {
            try
            {
                historyHttpService.SetTickLastCall(functionDeployment, DateTime.UtcNow.Ticks);
                if (!processing.Task.IsCompleted)
                {
                    continue;
                }

                HttpResponseMessage httpResponseMessage = processing.Task.Result;
                var statusCode = (int)httpResponseMessage.StatusCode;

                httpResponseMessage.Dispose();
                logger.LogDebug(
                    "{CustomRequestMethod}: /async-function/{CustomRequestPath}{CustomRequestQuery} {StatusCode}",
                    processing.CustomRequest.Method, processing.CustomRequest.Path, processing.CustomRequest.Query,
                    httpResponseMessage.StatusCode);
                httpResponseMessagesToDelete.Add(processing);
                queueItemStatusList.Add(new QueueItemStatus(processing.id, statusCode));
            }
            catch (Exception e)
            {
                queueItemStatusList.Add(new QueueItemStatus(processing.id, 500));
                httpResponseMessagesToDelete.Add(processing);
                logger.LogWarning("Request Error: {Message} {StackTrace}", e.Message, e.StackTrace);
                historyHttpService.SetTickLastCall(functionDeployment, DateTime.UtcNow.Ticks);
            }
        }

        foreach (RequestToWait httpResponseMessage in httpResponseMessagesToDelete)
        {
            processingTasks[functionDeployment].Remove(httpResponseMessage);
        }
        await slimFaasQueue.ListSetQueueItemStatusAsync(functionDeployment, listQueueItemStatus);

        int numberProcessingTasks = processingTasks[functionDeployment].Count;
        return numberProcessingTasks;
    }
}
