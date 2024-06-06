using System.Globalization;
using SlimFaas.Kubernetes;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Security.Cryptography.X509Certificates;

namespace SlimFaas;

public interface IReplicasService
{
    DeploymentsInformations Deployments { get; }
    Task SyncDeploymentsFromSlimData(DeploymentsInformations deploymentsInformations);
    Task<DeploymentsInformations> SyncDeploymentsAsync(string kubeNamespace);
    Task CheckScaleAsync(string kubeNamespace);
}

public class ReplicasService(IKubernetesService kubernetesService,
        HistoryHttpMemoryService historyHttpService,
        ILogger<ReplicasService> logger)
    : IReplicasService
{
    private readonly bool _isTurnOnByDefault = EnvironmentVariables.ReadBoolean(logger,
        EnvironmentVariables.PodScaledUpByDefaultWhenInfrastructureHasNeverCalled,
        EnvironmentVariables.PodScaledUpByDefaultWhenInfrastructureHasNeverCalledDefault);

    private readonly object Lock = new();

    private DeploymentsInformations _deployments = new(new List<DeploymentInformation>(),
        new SlimFaasDeploymentInformation(1, new List<PodInformation>()), new List<PodInformation>());

    public DeploymentsInformations Deployments
    {
        get
        {
            lock (Lock)
            {
                return new DeploymentsInformations(_deployments.Functions.ToArray(),
                    new SlimFaasDeploymentInformation(_deployments?.SlimFaas?.Replicas ?? 1,
                        _deployments?.SlimFaas?.Pods ?? new List<PodInformation>()), new List<PodInformation>());
            }
        }
    }

    public async Task SyncDeploymentsFromSlimData(DeploymentsInformations deploymentsInformations)
    {
        lock (Lock)
        {
            _deployments = deploymentsInformations;
        }
    }

    public async Task<DeploymentsInformations> SyncDeploymentsAsync(string kubeNamespace)
    {
        DeploymentsInformations deployments = await kubernetesService.ListFunctionsAsync(kubeNamespace);
        lock (Lock)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                    foreach (DeploymentInformation deploymentInformation in deployments.Functions)
                    {
                        var currentDeployment = _deployments.Functions.FirstOrDefault(f =>
                            f.Deployment == deploymentInformation.Deployment &&
                            f.ResourceVersion == deploymentInformation.ResourceVersion);
                        if (currentDeployment == null)
                        {
                            // Un log information avec toutes les informations de toutes les propriété de la fonction
                            logger.LogInformation("New deployment {Deployment} \n" +
                                                  "with {Replicas} replicas \n" +
                                                  "with {ReplicasAtStart} replicas at start \n" +
                                                  "with {ReplicasMin} replicas min \n" +
                                                  "with {ReplicasStartAsSoonAsOneFunctionRetrieveARequest} replicas start as soon as one function retrieve a request \n" +
                                                  "with {TimeoutSecondBeforeSetReplicasMin} timeout second before set replicas min \n" +
                                                  "with {PodType} pod type \n" +
                                                  "with {ResourceVersion} resource version \n"+
                                                  "with {NumberParallelRequest} number parallel request \n",
                                deploymentInformation.Deployment, deploymentInformation.Replicas, deploymentInformation.ReplicasAtStart, deploymentInformation.ReplicasMin,
                                deploymentInformation.ReplicasStartAsSoonAsOneFunctionRetrieveARequest, deploymentInformation.TimeoutSecondBeforeSetReplicasMin,
                                deploymentInformation.PodType, deploymentInformation.ResourceVersion, deploymentInformation.NumberParallelRequest);

                        }
                    }

            }


            _deployments = deployments;
        }
        return deployments;
    }

    public async Task CheckScaleAsync(string kubeNamespace)
    {
        long maximumTicks = 0L;
        var ticksLastCall = new Dictionary<string, long>();
        foreach (DeploymentInformation deploymentInformation in Deployments.Functions)
        {
            long tickLastCall = historyHttpService.GetTicksLastCall(deploymentInformation.Deployment);
            ticksLastCall.Add(deploymentInformation.Deployment, tickLastCall);
            maximumTicks = Math.Max(maximumTicks, tickLastCall);
        }

        List<Task<ReplicaRequest?>> tasks = new();
        foreach (DeploymentInformation deploymentInformation in Deployments.Functions)
        {
            long tickLastCall = deploymentInformation.ReplicasStartAsSoonAsOneFunctionRetrieveARequest
                ? maximumTicks
                : ticksLastCall[deploymentInformation.Deployment];

            if (_isTurnOnByDefault && tickLastCall == 0)
            {
                tickLastCall = DateTime.UtcNow.Ticks;
            }

            var lastTicksFromSchedule = GetLastTicksFromSchedule(deploymentInformation, DateTime.UtcNow);
            if (lastTicksFromSchedule.HasValue && lastTicksFromSchedule > tickLastCall)
            {
                tickLastCall = lastTicksFromSchedule.Value;
            }

            var allDependsOn = Deployments.Functions
                .Where(f => f.DependsOn != null && f.DependsOn.Contains(deploymentInformation.Deployment))
                .ToList();

            foreach (DeploymentInformation information in allDependsOn)
            {
                if(tickLastCall < ticksLastCall[information.Deployment])
                    tickLastCall = ticksLastCall[information.Deployment];
            }

            var timeToWaitSeconds = TimeSpan.FromSeconds(GetTimeoutSecondBeforeSetReplicasMin(deploymentInformation, DateTime.UtcNow));
            bool timeElapsedWithoutRequest = (TimeSpan.FromTicks(tickLastCall) +
                timeToWaitSeconds) < TimeSpan.FromTicks(DateTime.UtcNow.Ticks);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                var time = (TimeSpan.FromTicks(tickLastCall) +
                            timeToWaitSeconds) -
                           TimeSpan.FromTicks(DateTime.UtcNow.Ticks);
                logger.LogDebug(
                    "Time left without request for scale down {Deployment} is {TimeElapsedWithoutRequest}",
                    deploymentInformation.Deployment, time);
            }
            int currentScale = deploymentInformation.Replicas;
            if (timeElapsedWithoutRequest)
            {
                if (currentScale == deploymentInformation.ReplicasMin)
                {
                    continue;
                } else if(currentScale < deploymentInformation.ReplicasMin)
                {
                    logger.LogInformation("Scale up {Deployment} from {currentScale} to {Replica at start}", deploymentInformation.Deployment, currentScale, deploymentInformation.ReplicasAtStart);
                }
                else {
                    logger.LogInformation("Scale down {Deployment} from {currentScale} to {ReplicasMin}", deploymentInformation.Deployment, currentScale, deploymentInformation.ReplicasMin);
                }
                Console.WriteLine("Time elapsed without request reached. Scaling...");

                Task<ReplicaRequest?> task = kubernetesService.ScaleAsync(new ReplicaRequest(
                    Replicas: deploymentInformation.ReplicasMin,
                    Deployment: deploymentInformation.Deployment,
                    Namespace: kubeNamespace,
                    PodType: deploymentInformation.PodType
                ));

                tasks.Add(task);
            }
            else if ((currentScale is 0 || currentScale < deploymentInformation.ReplicasMin) && DependsOnReady(deploymentInformation))
            {
                logger.LogInformation("Scale up {Deployment} from {currentScale} to {Replica at start}", deploymentInformation.Deployment, currentScale, deploymentInformation.ReplicasAtStart);
                Task<ReplicaRequest?> task = kubernetesService.ScaleAsync(new ReplicaRequest(
                    Replicas: deploymentInformation.ReplicasAtStart,
                    Deployment: deploymentInformation.Deployment,
                    Namespace: kubeNamespace,
                    PodType: deploymentInformation.PodType
                ));

                tasks.Add(task);
            }
        }

        if (tasks.Count <= 0)
        {
            return;
        }

        List<DeploymentInformation> updatedFunctions = new();
        ReplicaRequest?[] replicaRequests = await Task.WhenAll(tasks);
        foreach (DeploymentInformation function in Deployments.Functions)
        {
            ReplicaRequest? updatedFunction = replicaRequests.ToList().Find(t => t?.Deployment == function.Deployment);
            updatedFunctions.Add(function with { Replicas = updatedFunction?.Replicas ?? function.Replicas });
        }

        lock (Lock)
        {
            _deployments = Deployments with { Functions = updatedFunctions };
        }
    }

    record TimeToScaleDownTimeout(int Hours, int Minutes, int Value);

    private static DateTime CreateDateTime(DateTime dateTime, int hours, int minutes, string culture)
    {
        return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, hours, minutes, 0, new CultureInfo(culture).Calendar).ToUniversalTime();
    }

    public static long? GetLastTicksFromSchedule(DeploymentInformation deploymentInformation, DateTime nowUtc)
    {
        if (deploymentInformation.Schedule is not { Default: not null })
        {
            return null;
        }

        var dateTime = DateTime.MinValue;
        var dates = new List<DateTime>();
        
        Console.WriteLine(
            "Culture : "
          + deploymentInformation.Schedule.Culture
          + ", WakeUp : "
          + string.Join(',', deploymentInformation.Schedule.Default.WakeUp)
          + ", ScaleDownTimeout : "
          + string.Join(',', deploymentInformation.Schedule.Default.ScaleDownTimeout));

        foreach (var defaultSchedule in deploymentInformation.Schedule.Default.WakeUp)
        {
            var splits = defaultSchedule.Split(':');
            if (splits.Length != 2)
            {
                continue;
            }

            if (!int.TryParse(splits[0], out int hours) || !int.TryParse(splits[1], out int minutes))
            {
                continue;
            }

            var date = CreateDateTime(nowUtc, hours, minutes, deploymentInformation.Schedule.Culture);
            Console.WriteLine("Created date from schedule : " + date.ToString());
            dates.Add(date);
        }


        foreach (var date in dates)
        {
            if (date <= nowUtc && date > dateTime)
            {
                dateTime = date;
            }
        }

        if (dateTime > DateTime.MinValue)
        {
            Console.WriteLine("LastTicksFromSchedule, dateTime > DateTime.MinValue : " + dateTime.Ticks);
            return dateTime.Ticks;
        }

        if(dateTime == DateTime.MinValue && dates.Count > 0)
        {
            dateTime = dates.OrderBy(d => d).Last();
            Console.WriteLine("LastTicksFromSchedule, dateTime == DateTime.MinValue && dates.Count > 0 : " + dateTime.AddDays(-1).Ticks);
            return dateTime.AddDays(-1).Ticks;
        }

        return null;
    }

    public static int GetTimeoutSecondBeforeSetReplicasMin(DeploymentInformation deploymentInformation, DateTime nowUtc)
    {
        if (deploymentInformation.Schedule is { Default: not null })
        {
            List<TimeToScaleDownTimeout> times = new();
            foreach (var defaultSchedule in deploymentInformation.Schedule.Default.ScaleDownTimeout)
            {
                Console.WriteLine("Default schedule : " + defaultSchedule.Time);
                Console.WriteLine("With Value being " + defaultSchedule.Value);
                var splits = defaultSchedule.Time.Split(':');
                if (splits.Length != 2)
                {
                    continue;
                }
                if (!int.TryParse(splits[0], out int hours) || !int.TryParse(splits[1], out int minutes))
                {
                    continue;
                }

                var date = CreateDateTime(nowUtc, hours, minutes, deploymentInformation.Schedule.Culture);
                times.Add(new TimeToScaleDownTimeout(date.Hour, date.Minute, defaultSchedule.Value));
            }
            Console.WriteLine("Total times found : " + times.Count);
            Console.WriteLine("times :");
            foreach (var time in times)
            {
                Console.WriteLine(time.ToString());
            }

            if (times.Count >= 2)
            {
                /* 
                    Convert to ticks to prevent schedule elements, when moving to utc time, from taking precedence when they shoudln't.
                    For instance: 1am in French would become 11pm of the day before in utc hour.
                    This would make it take precedence over almost every other time.
                    Therefore, comparing only the total amount of minutes, as was done before, would not work.
                */
                List<TimeToScaleDownTimeout> orderedTimes = times
                    .Select(t => new {Time = t, CreateDateTime(nowUtc, t.Hours, t.Minutes, deploymentInformation.Schedule.Culture).ToUniversalTime().Ticks})
                    .Where(t => t.Ticks < nowUtc.Ticks)
                    .OrderBy(t => t.Ticks)
                    .Select(t => t.Time)
                    .ToList();
                if (orderedTimes.Count >= 1)
                {
                    Console.WriteLine("Selected orderedTime : " + orderedTimes[^1].Value);
                    Console.WriteLine("Count ordered times : " + orderedTimes.Count);
                    return orderedTimes[^1].Value;
                }

                Console.WriteLine("Ordered times :");
                foreach (var orderedTime in orderedTimes)
                {
                    Console.WriteLine(orderedTime.Hours + ":" + orderedTime.Minutes + ", value : " + orderedTime.Value);
                }

                
                return times.OrderBy(t => CreateDateTime(nowUtc, t.Hours, t.Minutes, deploymentInformation.Schedule.Culture).ToUniversalTime().Ticks).Last().Value;
            }
            else if (times.Count == 1)
            {
                var time = times.First();
                return (CreateDateTime(nowUtc, time.Hours, time.Minutes, deploymentInformation.Schedule.Culture).ToUniversalTime().Ticks < nowUtc.Ticks) ?
                    time.Value : deploymentInformation.TimeoutSecondBeforeSetReplicasMin;
            }
        }

        return deploymentInformation.TimeoutSecondBeforeSetReplicasMin;
    }

    private bool DependsOnReady(DeploymentInformation deploymentInformation)
    {
        if (deploymentInformation.DependsOn == null)
        {
            return true;
        }

        foreach (string dependOn in deploymentInformation.DependsOn)
        {
            if (Deployments.Functions.Where(f => f.Deployment == dependOn)
                .Any(f => f.Pods.Count(p => p.Ready.HasValue && p.Ready.Value) < f.ReplicasAtStart ))
            {
                return false;
            }
        }

        return true;
    }
}
