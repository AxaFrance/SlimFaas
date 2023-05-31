
using System.Text.Json;

namespace SlimFaas;


record struct RequestToWait(Task<HttpResponseMessage> Task, CustomRequest CustomRequest);

public class SlimWorker : BackgroundService
{
    private readonly HistoryHttpMemoryService _historyHttpService;
    private readonly ILogger<SlimWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IQueue _queue;
    private readonly ReplicasService _replicasService;

    private readonly IDictionary<string, IList<RequestToWait>> _processingTasks = new Dictionary<string, IList<RequestToWait>>();

    public SlimWorker(IQueue queue, ReplicasService replicasService, HistoryHttpMemoryService historyHttpService, ILogger<SlimWorker> logger, IServiceProvider serviceProvider)
    {
        _historyHttpService = historyHttpService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _queue = queue;
        _replicasService = replicasService;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var setTickLastCallCounterDictionnary = new Dictionary<string, int>();
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                await Task.Delay(10, stoppingToken);
                var deployments = _replicasService.Deployments;
                var functions = deployments.Functions;
                var slimFaas = deployments.SlimFaas;
                foreach (var function in functions)
                {
                    var functionDeployment = function.Deployment;
                    if (_processingTasks.ContainsKey(functionDeployment) == false)
                    {
                        _processingTasks.Add(functionDeployment, new List<RequestToWait>());
                    }

                    setTickLastCallCounterDictionnary.TryAdd(functionDeployment, 0);

                    var httpResponseMessagesToDelete = new List<RequestToWait>();
                    foreach (var processing in _processingTasks[functionDeployment])
                    {
                        try
                        {
                            if (!processing.Task.IsCompleted) continue;
                            var httpResponseMessage = processing.Task.Result;
                            httpResponseMessage.Dispose();
                            _logger.LogInformation("{CustomRequestMethod}: /async-function/{CustomRequestPath}{CustomRequestQuery} {StatusCode}", processing.CustomRequest.Method, processing.CustomRequest.Path, processing.CustomRequest.Query, httpResponseMessage.StatusCode);
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
                        _processingTasks[functionDeployment].Remove(httpResponseMessage);
                    }

                    var numberProcessingTasks = _processingTasks[functionDeployment].Count;

                    int? numberLimitProcessingTasks;
                    var numberReplicas = slimFaas.Replicas ?? 0;
                    
                    if (function.NumberParallelRequest < numberReplicas || numberReplicas == 0)
                    {
                        numberLimitProcessingTasks = numberReplicas;
                    }
                    else
                    {
                        numberLimitProcessingTasks = function.NumberParallelRequest / slimFaas.Replicas;    
                    }
                    
                    setTickLastCallCounterDictionnary[functionDeployment]++;
                    var functionReplicas = function.Replicas;
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
                    
                    if (functionReplicas == 0)
                    {
                        continue;
                    }

                    if (numberProcessingTasks >= numberLimitProcessingTasks) continue;

                    var numberTasksToDequeue = numberLimitProcessingTasks - numberProcessingTasks;
                    var jsons = await _queue.DequeueAsync(functionDeployment, numberTasksToDequeue.HasValue ? (long)numberTasksToDequeue: 1);
                    foreach (var requestJson in jsons)
                    {
                        var customRequest = JsonSerializer.Deserialize(requestJson, CustomRequestSerializerContext.Default.CustomRequest);
                        _logger.LogInformation("{CustomRequestMethod}: {CustomRequestPath}{CustomRequestQuery} Sending", customRequest.Method, customRequest.Path, customRequest.Query);
                        _logger.LogInformation("{RequestJson}", requestJson);
                        _historyHttpService.SetTickLastCall(functionDeployment, DateTime.Now.Ticks);
                        using var scope = _serviceProvider.CreateScope();
                        var taskResponse = scope.ServiceProvider.GetRequiredService<SendClient>()
                            .SendHttpRequestAsync(customRequest);
                        _processingTasks[functionDeployment].Add(new RequestToWait(taskResponse, customRequest));
                    }
                }
            }
            catch (Exception e)
            {
                 _logger.LogError(e, "Global Error in SlimFaas Worker");
            }
        }
    }
}