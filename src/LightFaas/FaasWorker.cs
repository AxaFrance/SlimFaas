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
    private readonly IDictionary<string, IList<RequestToWait>> _processingTasks = new Dictionary<string, IList<RequestToWait>>();

    public FaasWorker(IQueue queue, IServiceProvider serviceProvider)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
        {
            await Task.Delay(10);
            foreach (var queueKey in _queue.Keys)
            {
                using var scope = _serviceProvider.CreateScope();
                var faasLogger = scope.ServiceProvider.GetRequiredService<ILogger<FaasWorker>>();
                 if(_processingTasks.ContainsKey(queueKey.Key) == false)
                 {
                     _processingTasks.Add(queueKey.Key, new List<RequestToWait>());
                 }
                 var httpResponseMessagesToDelete = new List<RequestToWait>();
                 foreach (var processing in _processingTasks[queueKey.Key])
                 {
                     if (!processing.Task.IsCompleted) continue;
                     var httpResponseMessage = processing.Task.Result;
                     faasLogger.LogInformation($"{processing.CustomRequest.Method}: {processing.CustomRequest.Path}{processing.CustomRequest.Query} {httpResponseMessage.StatusCode}");
                     httpResponseMessagesToDelete.Add(processing);
                 }

                 foreach (var httpResponseMessage in httpResponseMessagesToDelete)
                 {
                     _processingTasks[queueKey.Key].Remove(httpResponseMessage);
                 }
                 
                 if(_processingTasks[queueKey.Key].Count >= queueKey.NumberParallel) continue;
                
                 var data = _queue.DequeueAsync(queueKey.Key);
                 if (string.IsNullOrEmpty(data)) continue;
                 var customRequest = JsonSerializer.Deserialize<CustomRequest>(data);
                 faasLogger.LogInformation($"{customRequest.Method}: {customRequest.Path}{customRequest.Query} Sending");
                 var taskResponse = scope.ServiceProvider.GetRequiredService<SendClient>().SendHttpRequestAsync(customRequest);
                 _processingTasks[queueKey.Key].Add(new RequestToWait(){ Task = taskResponse, CustomRequest = customRequest});
            }
        }
    }
}