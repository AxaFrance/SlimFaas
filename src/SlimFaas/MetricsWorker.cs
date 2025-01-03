using SlimFaas.Database;

namespace SlimFaas;

public class MetricsWorker(IReplicasService replicasService, ISlimFaasQueue slimFaasQueue, DynamicGaugeService dynamicGaugeService,
        ILogger<MetricsWorker> logger,
        int delay = EnvironmentVariables.ScaleReplicasWorkerDelayMillisecondsDefault)
    : BackgroundService
{
    private readonly int _delay =
        EnvironmentVariables.ReadInteger(logger, EnvironmentVariables.ScaleReplicasWorkerDelayMilliseconds, delay);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                await Task.Delay(_delay, stoppingToken);
                var deployments = replicasService.Deployments;
                foreach (var deployment in deployments.Functions)
                {
                    var numberElement = await slimFaasQueue.CountElementAsync(deployment.Deployment);
                    dynamicGaugeService.SetGaugeValue(
                        $"slimfaas_queue_{deployment.Deployment.ToLowerInvariant()}_length",
                        numberElement, "Current number of elements in the queue");
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Global Error in MetricsWorker");
            }
        }
    }
}
