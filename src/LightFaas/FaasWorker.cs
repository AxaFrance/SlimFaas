using System.Text.Json;
using LightFaas;

namespace LightFaas;


record RequestToWait
{
    public Task<HttpResponseMessage> Task { get; set; }
    public CustomRequest CustomRequest { get; set; }
}

public class FaasWorker : BackgroundService
{
    private readonly HistoryHttpService _historyHttpService;
    private readonly IQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ReplicasService _replicasService;

    //private readonly IDictionary<string, long> _lastHttpCall = new Dictionary<string, long>();
    private readonly IDictionary<string, IList<RequestToWait>> _processingTasks = new Dictionary<string, IList<RequestToWait>>();
    private readonly string _namespace;

    public FaasWorker(IQueue queue, IServiceProvider serviceProvider, ReplicasService replicasService, HistoryHttpService historyHttpService)
    {
        _historyHttpService = historyHttpService;
        _queue = queue;
        _serviceProvider = serviceProvider;
        _replicasService = replicasService;
        
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
                foreach (var function in _replicasService.Functions)
                {
                   using var scope = _serviceProvider.CreateScope();
                    var faasLogger = scope.ServiceProvider.GetRequiredService<ILogger<FaasWorker>>();
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
                            faasLogger.LogInformation(
                                $"{processing.CustomRequest.Method}: /async-function/{processing.CustomRequest.Path}{processing.CustomRequest.Query} {httpResponseMessage.StatusCode}");
                            httpResponseMessagesToDelete.Add(processing);
                            _historyHttpService.SetTickLastCall(functionDeployment, DateTime.Now.Ticks);
                        }
                        catch (Exception e)
                        {
                            httpResponseMessagesToDelete.Add(processing);
                            faasLogger.LogError("Request Error: " + e.Message + " " + e.StackTrace);
                            _historyHttpService.SetTickLastCall(functionDeployment, DateTime.Now.Ticks);
                        }
                    }

                    foreach (var httpResponseMessage in httpResponseMessagesToDelete)
                    {
                        _processingTasks[functionDeployment].Remove(httpResponseMessage);
                    }

                    if (_processingTasks[functionDeployment].Count >= function.NumberParallelRequest) continue;

                    var data = _queue.DequeueAsync(functionDeployment);
                    if (string.IsNullOrEmpty(data)) continue;
                    var customRequest = JsonSerializer.Deserialize<CustomRequest>(data);
                    faasLogger.LogInformation(
                        $"{customRequest.Method}: {customRequest.Path}{customRequest.Query} Sending");
                    _historyHttpService.SetTickLastCall(functionDeployment, DateTime.Now.Ticks);

                    var taskResponse = scope.ServiceProvider.GetRequiredService<SendClient>()
                        .SendHttpRequestAsync(customRequest);
                    _processingTasks[functionDeployment].Add(new RequestToWait()
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