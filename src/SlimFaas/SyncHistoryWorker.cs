namespace SlimFaas;

public class SyncHistoryWorker: BackgroundService
{
    private readonly ReplicasService _replicasService;
    private readonly HistoryHttpMemoryService _historyHttpMemoryService;
    private readonly HistoryHttpRedisService _historyHttpRedisService;
    private readonly ILogger<ReplicasSyncWorker> _logger;

    public SyncHistoryWorker(ReplicasService replicasService, 
        HistoryHttpMemoryService historyHttpMemoryService, 
        HistoryHttpRedisService historyHttpRedisService, 
        ILogger<ReplicasSyncWorker> logger)
    {
        _replicasService = replicasService;
        _historyHttpMemoryService = historyHttpMemoryService;
        _historyHttpRedisService = historyHttpRedisService;
        _logger = logger;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                await Task.Delay(500, stoppingToken);

                foreach (var function in _replicasService.Functions)
                {
                    var ticksRedis = _historyHttpRedisService.GetTicksLastCall(function.Deployment);
                    var ticksMemory = _historyHttpMemoryService.GetTicksLastCall(function.Deployment);
                    if(ticksRedis > ticksMemory)
                    {
                        _historyHttpMemoryService.SetTickLastCall(function.Deployment, ticksRedis);
                    } else if(ticksRedis < ticksMemory)
                    {
                        _historyHttpRedisService.SetTickLastCall(function.Deployment, ticksMemory);
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
