namespace SlimFaas;

public class MasterWorker : BackgroundService
{
    private readonly IMasterService _masterService;
    private readonly ILogger<MasterWorker> _logger;

    public MasterWorker(IMasterService masterService, ILogger<MasterWorker> logger)
    {
        _masterService = masterService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                await Task.Delay(1000, stoppingToken);
                await _masterService.CheckAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Global Error in MasterWorker");
            }
        }
       
    }
}