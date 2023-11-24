namespace SlimFaas;

public class HistorySynchronizationWorker(IReplicasService replicasService,
    HistoryHttpMemoryService historyHttpMemoryService,
    HistoryHttpRedisService historyHttpRedisService,
    ILogger<HistorySynchronizationWorker> logger,
    SlimDataStatus slimDataStatus,
        int delay = EnvironmentVariables.HistorySynchronizationWorkerDelayMillisecondsDefault)
    : BackgroundService
{
    private readonly int _delay = EnvironmentVariables.ReadInteger(logger, EnvironmentVariables.HistorySynchronisationWorkerDelayMilliseconds, delay);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await slimDataStatus.WaitForReadyAsync();
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                await Task.Delay(_delay, stoppingToken);

                foreach (var function in replicasService.Deployments.Functions)
                {
                    var ticksRedis = await historyHttpRedisService.GetTicksLastCallAsync(function.Deployment);
                    var ticksMemory = historyHttpMemoryService.GetTicksLastCall(function.Deployment);
                    if(ticksRedis > ticksMemory)
                    {
                        historyHttpMemoryService.SetTickLastCall(function.Deployment, ticksRedis);
                    } else if(ticksRedis < ticksMemory)
                    {
                        await historyHttpRedisService.SetTickLastCallAsync(function.Deployment, ticksMemory);
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Global Error in ScaleReplicasWorker");
            }
        }
    }
}
