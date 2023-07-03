namespace SlimFaas;

public class HistorySynchronizationWorker: BackgroundService
{
    private readonly IReplicasService _replicasService;
    private readonly HistoryHttpMemoryService _historyHttpMemoryService;
    private readonly HistoryHttpRedisService _historyHttpRedisService;
    private readonly ILogger<HistorySynchronizationWorker> _logger;
    private readonly int _delay;

    public HistorySynchronizationWorker(IReplicasService replicasService,
        HistoryHttpMemoryService historyHttpMemoryService,
        HistoryHttpRedisService historyHttpRedisService,
        ILogger<HistorySynchronizationWorker> logger,
        int delay = EnvironmentVariables.HistorySynchronizationWorkerDelayMillisecondsDefault)
    {
        _replicasService = replicasService;
        _historyHttpMemoryService = historyHttpMemoryService;
        _historyHttpRedisService = historyHttpRedisService;
        _logger = logger;

        _delay = EnvironmentVariables.ReadInteger(logger, EnvironmentVariables.HistorySynchronisationWorkerDelayMilliseconds, delay);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                await Task.Delay(_delay, stoppingToken);

                foreach (var function in _replicasService.Deployments.Functions)
                {
                    var ticksRedis = await _historyHttpRedisService.GetTicksLastCallAsync(function.Deployment);
                    var ticksMemory = _historyHttpMemoryService.GetTicksLastCall(function.Deployment);
                    if(ticksRedis > ticksMemory)
                    {
                        _historyHttpMemoryService.SetTickLastCall(function.Deployment, ticksRedis);
                    } else if(ticksRedis < ticksMemory)
                    {
                        await _historyHttpRedisService.SetTickLastCallAsync(function.Deployment, ticksMemory);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Global Error in ScaleReplicasWorker");
            }
        }
    }
}
