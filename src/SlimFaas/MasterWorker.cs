namespace SlimFaas;

public class MasterWorker : BackgroundService
{
    private readonly MasterService _masterService;
    private readonly ILogger<MasterWorker> _logger;

    public MasterWorker(MasterService masterService, ILogger<MasterWorker> logger)
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
                _masterService.Check();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Global Error in MasterWorker");
            }
        }
       
    }
}