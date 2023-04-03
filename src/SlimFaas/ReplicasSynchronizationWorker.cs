namespace SlimFaas;

public class ReplicasSynchronizationWorker: BackgroundService
{
    private readonly ReplicasService _replicasService;
    private readonly ILogger<ReplicasSynchronizationWorker> _logger;
    private readonly string _namespace;

    public ReplicasSynchronizationWorker(ReplicasService replicasService, ILogger<ReplicasSynchronizationWorker> logger)
    {
        _replicasService = replicasService;
        _logger = logger;
        _namespace =
            Environment.GetEnvironmentVariable("NAMESPACE") ?? "default";
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                await _replicasService.SyncFunctionsAsync(_namespace);
                await Task.Delay(10000, stoppingToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Global Error in ScaleReplicasWorker");
            }
        }
    }
}
