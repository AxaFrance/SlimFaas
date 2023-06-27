namespace SlimFaas;

public class ReplicasSynchronizationWorker: BackgroundService
{
    private readonly IReplicasService _replicasService;
    private readonly ILogger<ReplicasSynchronizationWorker> _logger;
    private readonly int _delay;
    private readonly string _namespace;

    public ReplicasSynchronizationWorker(IReplicasService replicasService, ILogger<ReplicasSynchronizationWorker> logger, int delay = 2000)
    {
        _replicasService = replicasService;
        _logger = logger;
        _delay = int.Parse(Environment.GetEnvironmentVariable("REPLICAS_SYNCHRONISATION_WORKER_DELAY_MILLISECONDS")  ?? delay.ToString());;
        _namespace =
            Environment.GetEnvironmentVariable("NAMESPACE") ?? "default";
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                await Task.Delay(_delay, stoppingToken);
                await _replicasService.SyncDeploymentsAsync(_namespace);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Global Error in ScaleReplicasWorker");
            }
        }
    }
}
