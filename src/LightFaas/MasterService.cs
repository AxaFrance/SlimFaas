namespace LightFaas;

public class MasterService
{
    private readonly RedisService _redisService;
    private readonly string _id = Guid.NewGuid().ToString();
    public bool IsMaster { get; private set; }
    private const string lightFaasMaster = "lightfaas_master";
    private const string masterId = "master_id";
    private const string lastTicks = "last_ticks";

    public MasterService(RedisService redisService)
    {
        _redisService = redisService; 
    }

    public void Check()
    {
        var dictionary= _redisService.HashGetAll(lightFaasMaster);
        if (dictionary.Count == 0)
        {
            _redisService.HashSet(lightFaasMaster, new Dictionary<string, string>
            {
                { masterId, _id },
                { lastTicks, DateTime.Now.Ticks.ToString() },
            });
            return;
        }

        var currentMasterId = dictionary[masterId];
        var currentTicks = long.Parse(dictionary[lastTicks]);
        var isMaster = currentMasterId == _id;
        if (isMaster != IsMaster)
        {
            lock (this)
            {
                IsMaster = isMaster;
            }
        }

        var isMasterTimeElaped = TimeSpan.FromTicks(currentTicks) + TimeSpan.FromSeconds(2) < TimeSpan.FromTicks(DateTime.Now.Ticks);
        switch (isMaster)
        {
            case false when isMasterTimeElaped:
            case true when !isMasterTimeElaped:
                _redisService.HashSet(lightFaasMaster, new Dictionary<string, string>
                {
                    { masterId, _id },
                    { lastTicks, DateTime.Now.Ticks.ToString() },
                });
                break;
        }
    }

}