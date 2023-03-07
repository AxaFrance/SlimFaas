using System.Text.Json;

namespace WebApplication1;

public class FaasWorker : BackgroundService
{
    private readonly IQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDictionary<string, IList<Task<HttpResponseMessage>>> _processingTasks = new Dictionary<string, IList<Task<HttpResponseMessage>>>();

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
                ILogger<FaasWorker> faasLogger = scope.ServiceProvider.GetRequiredService<ILogger<FaasWorker>>();
                 if(_processingTasks.ContainsKey(queueKey.Key) == false)
                 {
                     _processingTasks.Add(queueKey.Key, new List<Task<HttpResponseMessage>>());
                 }
                 var httpResponseMessages = new List<Task<HttpResponseMessage>>();
                 foreach (var processing in _processingTasks[queueKey.Key])
                 {
                        if (processing.IsCompleted)
                        {
                            var httpResponseMessage = processing.Result;
                            faasLogger.LogInformation($"{httpResponseMessage.StatusCode}");
                            httpResponseMessages.Add(processing);
                        }
                 }

                 foreach (var httpResponseMessage in httpResponseMessages)
                 {
                     _processingTasks[queueKey.Key].Remove(httpResponseMessage);
                 }
                 
                 if(_processingTasks[queueKey.Key].Count >= queueKey.NumberParallel) continue;
                
                 var data = _queue.DequeueAsync(queueKey.Key);
                 if (string.IsNullOrEmpty(data)) continue;
                 var customRequest = JsonSerializer.Deserialize<CustomRequest>(data);
                 
                 var taskResponse = scope.ServiceProvider.GetRequiredService<SendClient>().SendHttpRequestAsync(customRequest);
                 _processingTasks[queueKey.Key].Add(taskResponse);
            }
        }
    }
}