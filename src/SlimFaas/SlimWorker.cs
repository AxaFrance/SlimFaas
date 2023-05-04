
using System.Text.Json;

namespace SlimFaas;


record RequestToWait
{
    public Task<HttpResponseMessage> Task { get; set; }
    public CustomRequest CustomRequest { get; set; }
}

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

                    var httpResponseMessagesToDelete = new List<RequestToWait>();
                    foreach (var processing in _processingTasks[functionDeployment])
                    {
                        try
                        {
                            if (!processing.Task.IsCompleted) continue;
                            var httpResponseMessage = processing.Task.Result;
                            _logger.LogInformation(
                                $"{processing.CustomRequest.Method}: /async-function/{processing.CustomRequest.Path}{processing.CustomRequest.Query} {httpResponseMessage.StatusCode}");
                            httpResponseMessagesToDelete.Add(processing);
                            _historyHttpService.SetTickLastCall(functionDeployment, DateTime.Now.Ticks);
                        }
                        catch (Exception e)
                        {
                            httpResponseMessagesToDelete.Add(processing);
                            _logger.LogWarning("Request Error: " + e.Message + " " + e.StackTrace);
                            _historyHttpService.SetTickLastCall(functionDeployment, DateTime.Now.Ticks);
                        }
                    }

                    foreach (var httpResponseMessage in httpResponseMessagesToDelete)
                    {
                        _processingTasks[functionDeployment].Remove(httpResponseMessage);
                    }

                    var numberProcessingTasks = _processingTasks[functionDeployment].Count;
                    var numberLimitProcessingTasks = function.NumberParallelRequest / slimFaas.Replicas;
                    if (numberProcessingTasks >= numberLimitProcessingTasks) continue;

                    if (function.Replicas == 0)
                    {
                        var queueLenght = _queue.Count(functionDeployment);
                        if (queueLenght > 0)
                        {
                            _historyHttpService.SetTickLastCall(functionDeployment, DateTime.Now.Ticks);
                            continue;
                        }
                    }

                    var numberTasksToDequeue = numberLimitProcessingTasks - numberProcessingTasks;
                    var datas = _queue.DequeueAsync(functionDeployment, numberTasksToDequeue.HasValue ? (long)numberTasksToDequeue: 1);
                    foreach (var data in datas)
                    {
                        var customRequest = JsonSerializer.Deserialize(data, CustomRequestSerializerContext.Default.CustomRequest);
                        _logger.LogInformation(
                            $"{customRequest.Method}: {customRequest.Path}{customRequest.Query} Sending");
                        _historyHttpService.SetTickLastCall(functionDeployment, DateTime.Now.Ticks);
                        using var scope = _serviceProvider.CreateScope();
                        var taskResponse = scope.ServiceProvider.GetRequiredService<SendClient>()
                            .SendHttpRequestAsync(customRequest);
                        _processingTasks[functionDeployment].Add(new RequestToWait()
                            { Task = taskResponse, CustomRequest = customRequest });
                    }
                }
            }
            catch (Exception e)
            {
                 _logger.LogError(e, "Global Error in FaasWorker");
            }
        }
    }
}