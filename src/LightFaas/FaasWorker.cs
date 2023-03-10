using System.Text.Json;

namespace WebApplication1;


record RequestToWait
{
    public Task<HttpResponseMessage> Task { get; set; }
    public CustomRequest CustomRequest { get; set; }
}

public class FaasWorker : BackgroundService
{
    private readonly IQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly KubernetesService _kubernetesService;
    private readonly IDictionary<string, IList<RequestToWait>> _processingTasks = new Dictionary<string, IList<RequestToWait>>();
    private readonly string _namespace;

    public FaasWorker(IQueue queue, IServiceProvider serviceProvider, KubernetesService kubernetesService)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _kubernetesService = kubernetesService;
        _namespace =
            Environment.GetEnvironmentVariable("NAMESPACE") ?? "default";
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                await Task.Delay(10);
                foreach (var queueKey in _queue.Keys)
                {
                    using var scope = _serviceProvider.CreateScope();
                    var faasLogger = scope.ServiceProvider.GetRequiredService<ILogger<FaasWorker>>();
                    if (_processingTasks.ContainsKey(queueKey.Key) == false)
                    {
                        _processingTasks.Add(queueKey.Key, new List<RequestToWait>());
                    }

                    var httpResponseMessagesToDelete = new List<RequestToWait>();
                    foreach (var processing in _processingTasks[queueKey.Key])
                    {
                        try
                        {
                            if (!processing.Task.IsCompleted) continue;
                            var httpResponseMessage = processing.Task.Result;
                            faasLogger.LogInformation(
                                $"{processing.CustomRequest.Method}: /async-function/{processing.CustomRequest.Path}{processing.CustomRequest.Query} {httpResponseMessage.StatusCode}");
                            httpResponseMessagesToDelete.Add(processing);
                            _kubernetesService.Scale(new ReplicaRequest(){Replicas = 0, Deployment = queueKey.Key, Namespace = _namespace});
                        } catch (Exception e)
                        {
                            httpResponseMessagesToDelete.Add(processing);
                            faasLogger.LogError("Request Error: " + e.Message + " " + e.StackTrace);
                            _kubernetesService.Scale(new ReplicaRequest(){Replicas = 0, Deployment = queueKey.Key, Namespace = _namespace});
                        }
                    }

                    foreach (var httpResponseMessage in httpResponseMessagesToDelete)
                    {
                        _processingTasks[queueKey.Key].Remove(httpResponseMessage);
                    }

                    if (_processingTasks[queueKey.Key].Count >= queueKey.NumberParallel) continue;

                    var data = _queue.DequeueAsync(queueKey.Key);
                    if (string.IsNullOrEmpty(data)) continue;
                    var customRequest = JsonSerializer.Deserialize<CustomRequest>(data);
                    faasLogger.LogInformation(
                        $"{customRequest.Method}: {customRequest.Path}{customRequest.Query} Sending");
                    
                    _kubernetesService.Scale(new ReplicaRequest(){Replicas = 1, Deployment = queueKey.Key, Namespace = _namespace});
                    var taskResponse = scope.ServiceProvider.GetRequiredService<SendClient>()
                        .SendHttpRequestAsync(customRequest);
                    _processingTasks[queueKey.Key].Add(new RequestToWait()
                        { Task = taskResponse, CustomRequest = customRequest });
                }
            }
            catch (Exception e)
            {
                using var scope = _serviceProvider.CreateScope();
                var faasLogger = scope.ServiceProvider.GetRequiredService<ILogger<FaasWorker>>();
                faasLogger.LogError("Global Error in FaasWorker: " + e.Message + " " + e.StackTrace);
            }
        }
    }
}