using System.Globalization;
using SlimFaas.Kubernetes;
using System.Text.Json.Serialization;
using System.Text.Json;

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
            if (lastTicksFromSchedule > tickLastCall)
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

            bool timeElapsedWithoutRequest = TimeSpan.FromTicks(tickLastCall) +
                                              TimeSpan.FromSeconds(GetTimeoutSecondBeforeSetReplicasMin(deploymentInformation, DateTime.UtcNow)) <
                                              TimeSpan.FromTicks(DateTime.UtcNow.Ticks);
            int currentScale = deploymentInformation.Replicas;
            logger.LogInformation("Current {Deployment} scale ", deploymentInformation.Deployment, currentScale);
            if (timeElapsedWithoutRequest)
            {
                logger.LogInformation("Time elapsed without request for {Deployment}", deploymentInformation.Deployment);
                if (currentScale <= deploymentInformation.ReplicasMin)
                {
                    continue;
                }

                logger.LogInformation("Scale down {Deployment} from {CurrentScale} to {ReplicasMin}", deploymentInformation.Deployment, currentScale, deploymentInformation.ReplicasMin);
                Task<ReplicaRequest?> task = kubernetesService.ScaleAsync(new ReplicaRequest(
                    Replicas: deploymentInformation.ReplicasMin,
                    Deployment: deploymentInformation.Deployment,
                    Namespace: kubeNamespace,
                    PodType: deploymentInformation.PodType
                ));

                tasks.Add(task);
            }
            else if (currentScale is 0 && DependsOnReady(deploymentInformation))
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
            return dateTime.Ticks;
        }

        if(dateTime == DateTime.MinValue && dates.Count > 0)
        {
            dateTime = dates.OrderBy(d => d).Last();
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
                times.Add( new TimeToScaleDownTimeout(date.Hour, date.Minute, defaultSchedule.Value));
            }

            if (times.Count >= 2)
            {
                List<TimeToScaleDownTimeout> orderedTimes = times
                    .Where(t => t.Hours*60 + t.Minutes < nowUtc.Hour*60 + nowUtc.Minute)
                    .OrderBy(t => t.Hours * 60+ t.Minutes)
                    .ToList();
                if (orderedTimes.Count >= 1)
                {
                    return orderedTimes[^1].Value;
                }

                return times.OrderBy(t => t.Hours * 60+ t.Minutes).Last().Value;
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
