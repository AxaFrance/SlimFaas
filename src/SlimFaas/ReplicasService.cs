namespace SlimFaas;

public class ReplicasService
{
    private readonly KubernetesService _kubernetesService;
    private readonly HistoryHttpService _historyHttpService;
    private readonly IServiceProvider _serviceProvider;
    private  IList<DeploymentInformation> _functions;

    public ReplicasService(KubernetesService kubernetesService, HistoryHttpService historyHttpService, IServiceProvider serviceProvider)
    {
        _kubernetesService = kubernetesService;
        _historyHttpService = historyHttpService;
        _serviceProvider = serviceProvider;
        _functions = new List<DeploymentInformation>();
    }

    public IList<DeploymentInformation> Functions
    {
        get => _functions;
    }
    
    public async Task SyncFunctionsAsync(string kubeNamespace)
    {
        var functions = await _kubernetesService.ListFunctionsAsync(kubeNamespace);
        lock (this)
        {
            _functions = functions;
        }
    }
    
    public async Task CheckScaleAsync(string kubeNamespace)
    {
        var maximumTicks = 0L;
        IDictionary<string, long> ticksLastCall = new Dictionary<string, long>();
        foreach (var deploymentInformation in _functions)
        {
            var tickLastCall = _historyHttpService.GetTicksLastCall(deploymentInformation.Deployment);
            ticksLastCall.Add(deploymentInformation.Deployment, tickLastCall);
            maximumTicks = Math.Max(maximumTicks, tickLastCall);
        }

        foreach (var deploymentInformation in _functions)
        {
            var tickLastCall = deploymentInformation.ReplicasStartAsSoonAsOneFunctionRetrieveARequest ? maximumTicks : ticksLastCall[deploymentInformation.Deployment];
            
            var timeElapsedWhithoutRequest = TimeSpan.FromTicks(tickLastCall) + TimeSpan.FromSeconds(deploymentInformation.TimeoutSecondBeforeSetReplicasMin) <
                    TimeSpan.FromTicks(DateTime.Now.Ticks);
            var currentScale = deploymentInformation.Replicas;
            if (timeElapsedWhithoutRequest)
            {
                if (currentScale.HasValue && currentScale > deploymentInformation.ReplicasMin)
                {
                    Task.Run(async () =>
                    {
                        var scope = _serviceProvider.CreateScope();
                        var kubernetesService = scope.ServiceProvider.GetService<KubernetesService>();
                        await kubernetesService.ScaleAsync(new ReplicaRequest()
                        {
                            Replicas = deploymentInformation.ReplicasMin, Deployment = deploymentInformation.Deployment,
                            Namespace = kubeNamespace
                        });
                    });

                }
            }
            else if (currentScale is 0)
            {
                Task.Run(async () =>
                {
                    var scope = _serviceProvider.CreateScope();
                    var kubernetesService = scope.ServiceProvider.GetService<KubernetesService>();
                    await kubernetesService.ScaleAsync(new ReplicaRequest()
                    { Replicas = deploymentInformation.ReplicasAtStart, Deployment = deploymentInformation.Deployment, Namespace = kubeNamespace });
                });

            }
        }
    }

}