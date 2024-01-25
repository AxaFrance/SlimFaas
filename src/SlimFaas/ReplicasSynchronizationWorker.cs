using System.Text.Json;
using SlimFaas.Kubernetes;

namespace SlimFaas;

public class ReplicasSynchronizationWorker(IReplicasService replicasService,
        IMasterService masterService,
        IDatabaseService slimDataService,
        ILogger<ReplicasSynchronizationWorker> logger,
        int delay = EnvironmentVariables.ReplicasSynchronizationWorkerDelayMillisecondsDefault)
    : BackgroundService
{
    private readonly int _delay = EnvironmentVariables.ReadInteger(logger,
        EnvironmentVariables.ReplicasSynchronisationWorkerDelayMilliseconds, delay);

    private readonly string _namespace = Environment.GetEnvironmentVariable(EnvironmentVariables.Namespace) ??
                                         EnvironmentVariables.NamespaceDefault;
    const string kubernetesDeployments = "kubernetes-deployments";
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                if(masterService.IsMaster == false)
                {
                    await Task.Delay(_delay/10, stoppingToken);
                    var currentDeploymentsJson = await slimDataService.GetAsync(kubernetesDeployments);
                    if (string.IsNullOrEmpty(currentDeploymentsJson))
                    {
                        return;
                    }
                    var deployments = JsonSerializer.Deserialize(currentDeploymentsJson, DeploymentsInformationsSerializerContext.Default.DeploymentsInformations);
                    if (deployments == null)
                    {
                        return;
                    }
                    await replicasService.SyncDeploymentsFromSlimData(deployments);
                }
                else
                {
                    await Task.Delay(_delay, stoppingToken);
                    var deployments = await replicasService.SyncDeploymentsAsync(_namespace);
                    var currentDeploymentsJson = await slimDataService.GetAsync(kubernetesDeployments);
                    var newDeploymentsJson = JsonSerializer.Serialize(deployments, DeploymentsInformationsSerializerContext.Default.DeploymentsInformations);
                    if (currentDeploymentsJson != newDeploymentsJson)
                    {
                        await slimDataService.SetAsync(kubernetesDeployments, newDeploymentsJson);
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Global Error in ScaleReplicasWorker");
            }
        }
    }
}
