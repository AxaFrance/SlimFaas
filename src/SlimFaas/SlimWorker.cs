using System.Text.Json;
using SlimFaas;

namespace SlimFaas;


record RequestToWait
{
    public Task<HttpResponseMessage> Task { get; set; }
    public CustomRequest CustomRequest { get; set; }
}

public class SlimWorker : BackgroundService
{
    private readonly HistoryHttpService _historyHttpService;
    private readonly ILogger<SlimWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IQueue _queue;
    private readonly ReplicasService _replicasService;

    private readonly IDictionary<string, IList<RequestToWait>> _processingTasks = new Dictionary<string, IList<RequestToWait>>();

    public SlimWorker(IQueue queue, ReplicasService replicasService, HistoryHttpService historyHttpService, ILogger<SlimWorker> logger, IServiceProvider serviceProvider)
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
                foreach (var function in _replicasService.Functions)
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

                    if (_processingTasks[functionDeployment].Count >= function.NumberParallelRequest) continue;

                    if (function.Replicas == 0)
                    {
                        var queueLenght = _queue.Count(functionDeployment);
                        if (queueLenght > 0)
                        {
                            _historyHttpService.SetTickLastCall(functionDeployment, DateTime.Now.Ticks);
                            continue;;
                        }
                    }
                    var data = _queue.DequeueAsync(functionDeployment);
                    if (string.IsNullOrEmpty(data)) continue;
                    var customRequest = JsonSerializer.Deserialize<CustomRequest>(data);
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
            catch (Exception e)
            {
                _logger.LogError(e, "Global Error in FaasWorker");
            }
        }
    }
}