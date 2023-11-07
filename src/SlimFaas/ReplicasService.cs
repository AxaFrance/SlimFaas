using SlimFaas.Kubernetes;

namespace SlimFaas;

public interface IReplicasService
{
    DeploymentsInformations Deployments { get; }
    Task SyncDeploymentsAsync(string kubeNamespace);
    Task CheckScaleAsync(string kubeNamespace);
}

public class ReplicasService : IReplicasService
{
    private readonly HistoryHttpMemoryService _historyHttpService;
    private readonly ILogger<ReplicasService> _logger;
    private readonly IKubernetesService _kubernetesService;
    private DeploymentsInformations _deployments;
    private readonly object Lock = new();
    private readonly bool _isTurnOnByDefault;

    public ReplicasService(IKubernetesService kubernetesService, HistoryHttpMemoryService historyHttpService, ILogger<ReplicasService> logger)
    {
        _kubernetesService = kubernetesService;
        _historyHttpService = historyHttpService;
        _logger = logger;
        _deployments = new DeploymentsInformations(Functions: new List<DeploymentInformation>(),
            SlimFaas: new SlimFaasDeploymentInformation(Replicas: 1, new List<PodInformation>()));

        _isTurnOnByDefault = EnvironmentVariables.ReadBoolean(logger, EnvironmentVariables.PodScaledUpByDefaultWhenInfrastructureHasNeverCalled, EnvironmentVariables.PodScaledUpByDefaultWhenInfrastructureHasNeverCalledDefault);
    }

    public DeploymentsInformations Deployments
    {
        get
        {
            lock (Lock)
            {
                return new DeploymentsInformations(Functions: _deployments.Functions.ToArray(),
                    SlimFaas: new SlimFaasDeploymentInformation(Replicas: _deployments?.SlimFaas?.Replicas ?? 1, new List<PodInformation>()));
            }
        }
    }

    public async Task SyncDeploymentsAsync(string kubeNamespace)
    {
        var deployments = await _kubernetesService.ListFunctionsAsync(kubeNamespace);
        if (deployments.Functions.Count <= 0)
        {
            return;
        }
        lock (Lock)
        {
            _deployments = deployments;
        }
    }

    public async Task CheckScaleAsync(string kubeNamespace)
    {
        var maximumTicks = 0L;
        IDictionary<string, long> ticksLastCall = new Dictionary<string, long>();
        foreach (var deploymentInformation in Deployments.Functions)
        {
            var tickLastCall = _historyHttpService.GetTicksLastCall(deploymentInformation.Deployment);
            ticksLastCall.Add(deploymentInformation.Deployment, tickLastCall);
            maximumTicks = Math.Max(maximumTicks, tickLastCall);
        }

        var tasks = new List<Task<ReplicaRequest?>>();
        foreach (var deploymentInformation in Deployments.Functions)
        {
            var tickLastCall = deploymentInformation.ReplicasStartAsSoonAsOneFunctionRetrieveARequest
                ? maximumTicks
                : ticksLastCall[deploymentInformation.Deployment];

            if(_isTurnOnByDefault && tickLastCall == 0)
            {
                tickLastCall = DateTime.Now.Ticks;
            }

            var timeElapsedWhithoutRequest = TimeSpan.FromTicks(tickLastCall) +
                                             TimeSpan.FromSeconds(deploymentInformation
                                                 .TimeoutSecondBeforeSetReplicasMin) <
                                             TimeSpan.FromTicks(DateTime.Now.Ticks);
            var currentScale = deploymentInformation.Replicas;

            if (timeElapsedWhithoutRequest)
            {
                if (currentScale <= deploymentInformation.ReplicasMin) continue;
                var task = _kubernetesService.ScaleAsync(new ReplicaRequest(
                Replicas : deploymentInformation.ReplicasMin,
                    Deployment : deploymentInformation.Deployment,
                    Namespace : kubeNamespace
                ));

                tasks.Add(task);
            }
            else if (currentScale is 0)
            {
                var task = _kubernetesService.ScaleAsync(new ReplicaRequest(
                    Replicas : deploymentInformation.ReplicasAtStart,
                    Deployment : deploymentInformation.Deployment,
                    Namespace : kubeNamespace
                ));

                tasks.Add(task);
            }
        }

        if (tasks.Count <= 0) return;

        var updatedFunctions = new List<DeploymentInformation>();
        ReplicaRequest?[] replicaRequests = await Task.WhenAll(tasks);
        foreach (var function in Deployments.Functions)
        {
            var updatedFunction = replicaRequests.ToList().Find(t => t?.Deployment == function.Deployment);
            updatedFunctions.Add(function with { Replicas = updatedFunction?.Replicas ?? function.Replicas });
        }
        lock (Lock)
        {
            _deployments = Deployments with { Functions = updatedFunctions };
        }
    }
}
