using SlimFaas.Database;
using SlimFaas.Kubernetes;

namespace SlimFaas;

public class HistorySynchronizationWorker(IReplicasService replicasService,
        HistoryHttpMemoryService historyHttpMemoryService,
        HistoryHttpDatabaseService historyHttpDatabaseService,
        ILogger<HistorySynchronizationWorker> logger,
        ISlimDataStatus slimDataStatus,
        int delay = EnvironmentVariables.HistorySynchronizationWorkerDelayMillisecondsDefault)
    : BackgroundService
{
    private readonly int _delay = EnvironmentVariables.ReadInteger(logger,
        EnvironmentVariables.HistorySynchronisationWorkerDelayMilliseconds, delay);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await slimDataStatus.WaitForReadyAsync();
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                await Task.Delay(_delay, stoppingToken);

                foreach (DeploymentInformation function in replicasService.Deployments.Functions)
                {
                    long ticksInDatabase = await historyHttpDatabaseService.GetTicksLastCallAsync(function.Deployment);
                    long ticksMemory = historyHttpMemoryService.GetTicksLastCall(function.Deployment);
                    if (ticksInDatabase > ticksMemory)
                    {
                        logger.LogDebug("HistorySynchronizationWorker: Synchronizing history for {Function} to {Ticks} from Database", function.Deployment, ticksInDatabase);
                        historyHttpMemoryService.SetTickLastCall(function.Deployment, ticksInDatabase);
                    }
                    else if (ticksInDatabase < ticksMemory)
                    {
                        logger.LogDebug("HistorySynchronizationWorker: Synchronizing history for {Function} to {Ticks} from Memory", function.Deployment, ticksMemory);
                        await historyHttpDatabaseService.SetTickLastCallAsync(function.Deployment, ticksMemory);
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Global Error in HistorySynchronizationWorker");
            }
        }
    }
}
