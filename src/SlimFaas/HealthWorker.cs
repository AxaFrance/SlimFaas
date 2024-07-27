using DotNext.Net.Cluster.Consensus.Raft;
using SlimFaas.Database;

namespace SlimFaas;

public class HealthWorker(IHostApplicationLifetime  hostApplicationLifetime, IRaftCluster raftCluster, ISlimDataStatus slimDataStatus,
        ILogger<HealthWorker> logger,
        int delay = EnvironmentVariables.HealthWorkerDelayMillisecondsDefault,
        int delayToExitSeconds = EnvironmentVariables.HealthWorkerDelayToExitSecondsDefault)
    : BackgroundService
{
    private readonly int _delay =
        EnvironmentVariables.ReadInteger(logger, EnvironmentVariables.HealthWorkerDelayMilliseconds, delay);
    private readonly int _delayToExitSeconds =
        EnvironmentVariables.ReadInteger(logger, EnvironmentVariables.HealthWorkerDelayToExitSeconds, delayToExitSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await slimDataStatus.WaitForReadyAsync();
        TimeSpan timeSpan = TimeSpan.FromSeconds(0);
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                await Task.Delay(_delay, stoppingToken);
                if (raftCluster.Leader == null)
                {
                    timeSpan = timeSpan.Add(TimeSpan.FromMilliseconds(_delay));
                    logger.LogWarning("Raft cluster has no leader");
                }
                else
                {
                    timeSpan = TimeSpan.FromSeconds(0);
                }

                if (timeSpan.TotalSeconds > _delayToExitSeconds)
                {
                    logger.LogError("Raft cluster has no leader for more than {TotalSeconds} seconds, exist the application ", timeSpan.TotalSeconds);
                    hostApplicationLifetime.StopApplication();
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Global Error in HealthWorker");
            }
        }
    }
}
