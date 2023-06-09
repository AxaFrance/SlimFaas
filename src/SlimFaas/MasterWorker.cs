namespace SlimFaas;

public class MasterWorker : BackgroundService
{
    private readonly IMasterService _masterService;
    private readonly ILogger<MasterWorker> _logger;
    private readonly int _delay;

    public MasterWorker(IMasterService masterService, ILogger<MasterWorker> logger, int delay = 1000)
    {
        _masterService = masterService;
        _logger = logger;
        _delay = delay;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                await Task.Delay(_delay, stoppingToken);
                await _masterService.CheckAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Global Error in MasterWorker");
            }
        }
       
    }
}