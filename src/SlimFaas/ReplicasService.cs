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
    private readonly IKubernetesService _kubernetesService;
    private DeploymentsInformations _deployments;

    public ReplicasService(IKubernetesService kubernetesService, HistoryHttpMemoryService historyHttpService)
    {
        _kubernetesService = kubernetesService;
        _historyHttpService = historyHttpService;
        _deployments = new DeploymentsInformations()
        {
            Functions = new List<DeploymentInformation>(),
            SlimFaas = new SlimFaasDeploymentInformation()
            {
                Replicas = 1
            }
        };
    }

    public DeploymentsInformations Deployments
    {
        get
        {
            lock (this)
            {
                return new DeploymentsInformations()
                {
                    Functions = _deployments.Functions.ToArray(),
                    SlimFaas = new SlimFaasDeploymentInformation()
                    {
                        Replicas = _deployments?.SlimFaas?.Replicas ?? 1
                    }
                };
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
        lock (this)
        {
            _deployments = deployments;
        }
    }

    public Task CheckScaleAsync(string kubeNamespace)
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

            var timeElapsedWhithoutRequest = TimeSpan.FromTicks(tickLastCall) +
                                             TimeSpan.FromSeconds(deploymentInformation
                                                 .TimeoutSecondBeforeSetReplicasMin) <
                                             TimeSpan.FromTicks(DateTime.Now.Ticks);
            var currentScale = deploymentInformation.Replicas;

            if (timeElapsedWhithoutRequest)
            {
                if (!currentScale.HasValue || !(currentScale > deploymentInformation.ReplicasMin)) continue;
                var task = _kubernetesService.ScaleAsync(new ReplicaRequest
                {
                    Replicas = deploymentInformation.ReplicasMin,
                    Deployment = deploymentInformation.Deployment,
                    Namespace = kubeNamespace
                });

                tasks.Add(task);
            }
            else if (currentScale is 0)
            {
                var task = _kubernetesService.ScaleAsync(new ReplicaRequest
                {
                    Replicas = deploymentInformation.ReplicasAtStart,
                    Deployment = deploymentInformation.Deployment, Namespace = kubeNamespace
                });

                tasks.Add(task);
            }
        }

        if (tasks.Count <= 0) return Task.CompletedTask;

        var updatedFunctions = new List<DeploymentInformation>();
        
        foreach (var function in Deployments.Functions)
        {
            var updatedFunction = tasks.FirstOrDefault(t => t.Result.Deployment == function.Deployment);
            updatedFunctions.Add(function with { Replicas = updatedFunction != null ? updatedFunction.Result.Replicas : function.Replicas });
        }
        lock (this)
        {
            _deployments = Deployments with { Functions = updatedFunctions };
        }

        return Task.CompletedTask;
    }
}