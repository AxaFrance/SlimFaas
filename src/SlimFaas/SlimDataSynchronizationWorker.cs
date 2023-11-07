using RaftNode;

namespace SlimFaas;

public class SlimDataSynchronizationWorker: BackgroundService
{
    private readonly IReplicasService _replicasService;
    private readonly ILogger<SlimDataSynchronizationWorker> _logger;
    private readonly int _delay;
    private bool _isStarted = false;

    public SlimDataSynchronizationWorker(IReplicasService replicasService, ILogger<SlimDataSynchronizationWorker> logger, int delay = EnvironmentVariables.ReplicasSynchronizationWorkerDelayMillisecondsDefault)
    {
        _replicasService = replicasService;
        _logger = logger;
        _delay = EnvironmentVariables.ReadInteger(logger, EnvironmentVariables.ReplicasSynchronisationWorkerDelayMilliseconds, delay);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                await Task.Delay(_delay, stoppingToken);
                // Start SlimData only when 2 replicas are in ready state

                if (_replicasService.Deployments.SlimFaas.Replicas >= 2 && _isStarted == false)
                {
                    _logger.LogInformation("SlimData is starting");
                    foreach (var pod in _replicasService.Deployments.SlimFaas.Pods)
                    {
#pragma warning disable CA2252
                        Startup.ClusterMembers.Add($"http://{pod.Ip}:3262");
#pragma warning restore CA2252
                    }

#pragma warning disable CA2252
                    await Starter.StartNode("http", 3262);
#pragma warning restore CA2252
                    _isStarted = true;
                }

            }
            catch (Exception e)
            {
                _logger.LogError(e, "Global Error in ScaleReplicasWorker");
            }
        }
    }
}
