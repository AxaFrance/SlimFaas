namespace LightFaas;

public class MasterService
{
    private readonly RedisService _redisService;
    private readonly string _id = Guid.NewGuid().ToString();
    public bool IsMaster { get; private set; }

    public MasterService(RedisService redisService)
    {
        _redisService = redisService; 
    }

    public void Check()
    {
        var dictionary= _redisService.HashGetAll("lightfaas_master");

        const string masterId = "master_id";
        const string lastTicks = "last_ticks";
        if (dictionary.Count == 0)
        {
            _redisService.HashSet("lightfaas_master", new Dictionary<string, string>
            {
                { masterId, _id },
                { lastTicks, DateTime.Now.Ticks.ToString() },
            });
            return;
        }

        var currentMasterId = dictionary[masterId];
        var currentTicks = long.Parse(dictionary[lastTicks]);
        lock (this)
        {
            IsMaster = currentMasterId == _id;
        }
        var isMasterTimeElaped = TimeSpan.FromTicks(currentTicks) + TimeSpan.FromSeconds(10) > TimeSpan.FromTicks(DateTime.Now.Ticks);
        switch (IsMaster)
        {
            case false when isMasterTimeElaped:
            case true when !isMasterTimeElaped:
                _redisService.HashSet("lightfaas_master", new Dictionary<string, string>
                {
                    { masterId, _id },
                    { lastTicks, DateTime.Now.Ticks.ToString() },
                });
                break;
        }
    }

}