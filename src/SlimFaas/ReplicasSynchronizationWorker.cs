using System.Text.Json;
using DotNext.Net.Cluster.Consensus.Raft;
using SlimFaas.Kubernetes;

namespace SlimFaas;

public class ReplicasSynchronizationWorker(IReplicasService replicasService,
    IRaftCluster cluster,
        IDatabaseService slimDataService,
        ILogger<ReplicasSynchronizationWorker> logger,
        int delay = EnvironmentVariables.ReplicasSynchronizationWorkerDelayMillisecondsDefault)
    : BackgroundService
{
    private readonly int _delay = EnvironmentVariables.ReadInteger(logger,
        EnvironmentVariables.ReplicasSynchronisationWorkerDelayMilliseconds, delay);

    private readonly string _namespace = Environment.GetEnvironmentVariable(EnvironmentVariables.Namespace) ??
                                         EnvironmentVariables.NamespaceDefault;
    public const string kubernetesDeployments = "kubernetes-deployments";
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                if(cluster is { Leader: not null, LeadershipToken.IsCancellationRequested: true } )
                {
                    await Task.Delay(_delay/10, stoppingToken);
                    var currentDeploymentsJson = await slimDataService.GetAsync(kubernetesDeployments);
                    Console.WriteLine(currentDeploymentsJson);
                    if (string.IsNullOrEmpty(currentDeploymentsJson))
                    {
                        return;
                    }
                    Console.WriteLine("ReplicasSynchronizationWorker: currentDeploymentsJson");
                    var deployments = JsonSerializer.Deserialize(currentDeploymentsJson, DeploymentsInformationsSerializerContext.Default.DeploymentsInformations);
                    if (deployments == null)
                    {
                        return;
                    }
                    Console.WriteLine("ReplicasSynchronizationWorker: SyncDeploymentsFromSlimData");
                    await replicasService.SyncDeploymentsFromSlimData(deployments);
                }
                else
                {
                    await Task.Delay(_delay, stoppingToken);
                    Console.WriteLine("ReplicasSynchronizationWorker: replicasService.SyncDeploymentsAsync");
                    var deployments = await replicasService.SyncDeploymentsAsync(_namespace);
                    if (cluster.Leader == null)
                    {
                        continue;
                    }
                    Console.WriteLine("ReplicasSynchronizationWorker: SyncDeploymentsAsync");
                    var currentDeploymentsJson = await slimDataService.GetAsync(kubernetesDeployments);
                    Console.WriteLine(currentDeploymentsJson);
                    var newDeploymentsJson = JsonSerializer.Serialize(deployments, DeploymentsInformationsSerializerContext.Default.DeploymentsInformations);
                    if (currentDeploymentsJson != newDeploymentsJson)
                    {
                        Console.WriteLine("ReplicasSynchronizationWorker: SetAsync");
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
