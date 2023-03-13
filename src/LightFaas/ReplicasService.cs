namespace LightFaas;

public class ReplicasService
{
    private readonly KubernetesService _kubernetesService;
    private readonly HistoryHttpService _historyHttpService;
    private  IList<DeploymentInformation> _functions;

    public ReplicasService(KubernetesService kubernetesService, HistoryHttpService historyHttpService)
    {
        _kubernetesService = kubernetesService;
        _historyHttpService = historyHttpService;
        _functions = new List<DeploymentInformation>();
    }

    public IList<DeploymentInformation> Functions
    {
        get => _functions;
    }
    
    public async Task CheckScaleAsync(string kubeNamespace)
    {
        var functions = await _kubernetesService.ListFunctionsAsync(kubeNamespace);
        lock (this)
        {
            _functions = functions;
        }

        foreach (var deploymentInformation in functions)
        {
            var tickLastCall = _historyHttpService.GetTicksLastCall(deploymentInformation.Deployment);
            var timeElapsedWhithoutRequest = TimeSpan.FromTicks(tickLastCall) + TimeSpan.FromSeconds(deploymentInformation.TimeoutSecondBeforeSetReplicasMin) <
                    TimeSpan.FromTicks(DateTime.Now.Ticks);
            var currentScale = deploymentInformation.Replicas;
            if (timeElapsedWhithoutRequest)
            {
                if (currentScale.HasValue && currentScale > deploymentInformation.ReplicasMin)
                {
                    await _kubernetesService.ScaleAsync(new ReplicaRequest()
                        { Replicas = deploymentInformation.ReplicasMin, Deployment = deploymentInformation.Deployment, Namespace = kubeNamespace });
                }
            }
            else
            {
                if (currentScale is 0)
                {
                    await _kubernetesService.ScaleAsync(new ReplicaRequest()
                        { Replicas = deploymentInformation.ReplicasAtStart, Deployment = deploymentInformation.Deployment, Namespace = kubeNamespace });
                }
            }



        }
    }

}