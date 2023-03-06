using System.Text.Json;

namespace WebApplication1;

public class FaasWorker : BackgroundService
{
    private readonly IQueue _queue;
    private readonly SendClient _sendClient;
    private readonly IDictionary<string, IList<Task<HttpResponseMessage>>> _processingTasks = new Dictionary<string, IList<Task<HttpResponseMessage>>>();

    public FaasWorker(IQueue queue, SendClient sendClient)
    {
        _queue = queue;
        _sendClient = sendClient;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
        {
            await Task.Delay(1);
            foreach (var queueKey in _queue.Keys)
            {
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
                            Console.WriteLine(httpResponseMessage.StatusCode);
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
                 var taskResponse = _sendClient.SendHttpRequestAsync(customRequest);
                 _processingTasks[queueKey.Key].Add(taskResponse);
            }
        }
    }
}