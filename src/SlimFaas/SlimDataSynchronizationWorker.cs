﻿using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.Http;
using SlimFaas.Database;
using SlimFaas.Kubernetes;

namespace SlimFaas;

public class SlimDataSynchronizationWorker(IReplicasService replicasService, IRaftCluster cluster,
        ILogger<SlimDataSynchronizationWorker> logger, ISlimDataStatus slimDataStatus,
        int delay = EnvironmentVariables.ReplicasSynchronizationWorkerDelayMillisecondsDefault)
    : BackgroundService
{
    private readonly int _delay = EnvironmentVariables.ReadInteger(logger,
        EnvironmentVariables.ReplicasSynchronisationWorkerDelayMilliseconds, delay);


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("SlimDataSynchronizationWorker: Start");
        await slimDataStatus.WaitForReadyAsync();
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                await Task.Delay(_delay, stoppingToken);
                // Start SlimData only when 2 replicas are in ready state
                if (cluster.LeadershipToken.IsCancellationRequested)
                {
                    continue;
                }

                foreach (PodInformation slimFaasPod in replicasService.Deployments.SlimFaas.Pods.Where(p =>
                             p.Started == true))
                {
                    string url = SlimDataEndpoint.Get(slimFaasPod);
                    if (cluster.Members.ToList().Any(m => m.EndPoint.ToString() == url))
                    {
                        continue;
                    }

                    Console.WriteLine($"SlimDataSynchronizationWorker: SlimFaas pod {slimFaasPod.Name} has to be added in the cluster");
                    await ((IRaftHttpCluster)cluster).AddMemberAsync(new Uri(url), stoppingToken);
                }

                foreach (IRaftClusterMember raftClusterMember in cluster.Members)
                {
                    if (replicasService.Deployments.SlimFaas.Pods.ToList().Any(slimFaasPod =>
                            SlimDataEndpoint.Get(slimFaasPod) == raftClusterMember.EndPoint.ToString()))
                    {
                        continue;
                    }

                    Console.WriteLine($"SlimDataSynchronizationWorker: SlimFaas pod {raftClusterMember.EndPoint.ToString()} need to be remove from the cluster");
                    await ((IRaftHttpCluster)cluster).RemoveMemberAsync(
                        new Uri(raftClusterMember.EndPoint.ToString() ?? string.Empty), stoppingToken);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Global Error in SlimDataSynchronizationWorker");
            }
        }
    }
}
