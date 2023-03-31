namespace SlimFaas;

public class ReplicasService
{
    private readonly HistoryHttpMemoryService _historyHttpService;
    private readonly IKubernetesService _kubernetesService;

    public ReplicasService(IKubernetesService kubernetesService, HistoryHttpMemoryService historyHttpService)
    {
        _kubernetesService = kubernetesService;
        _historyHttpService = historyHttpService;
        Functions = new List<DeploymentInformation>();
    }

    public IList<DeploymentInformation> Functions { get; private set; }

    public async Task SyncFunctionsAsync(string kubeNamespace)
    {
        var functions = await _kubernetesService.ListFunctionsAsync(kubeNamespace);
        if (functions == null)
        {
            return;
        }
        lock (this)
        {
            Functions = functions;
        }
    }

    public Task CheckScaleAsync(string kubeNamespace)
    {
        var maximumTicks = 0L;
        IDictionary<string, long> ticksLastCall = new Dictionary<string, long>();
        foreach (var deploymentInformation in Functions)
        {
            var tickLastCall = _historyHttpService.GetTicksLastCall(deploymentInformation.Deployment);
            ticksLastCall.Add(deploymentInformation.Deployment, tickLastCall);
            maximumTicks = Math.Max(maximumTicks, tickLastCall);
        }

        var tasks = new List<Task<ReplicaRequest>>();
        foreach (var deploymentInformation in Functions)
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
        
        Task.WaitAll(tasks.ToArray());
        var updatedFunctions = new List<DeploymentInformation>();
        lock (this)
        {
            foreach (var function in Functions)
            {
                var updatedFunction = tasks.FirstOrDefault(t => t.Result.Deployment == function.Deployment);
                updatedFunctions.Add(function with { Replicas = updatedFunction != null ? updatedFunction.Result.Replicas : function.Replicas });
            }
            Functions = updatedFunctions;
        }

        return Task.CompletedTask;
    }
}