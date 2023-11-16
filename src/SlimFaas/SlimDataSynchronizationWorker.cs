﻿using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.Http;
using RaftNode;

namespace SlimFaas;

public class SlimDataSynchronizationWorker(IReplicasService replicasService, IRaftCluster cluster,
        ILogger<SlimDataSynchronizationWorker> logger,
        int delay = EnvironmentVariables.ReplicasSynchronizationWorkerDelayMillisecondsDefault)
    : BackgroundService
{
    private readonly int _delay = EnvironmentVariables.ReadInteger<SlimDataSynchronizationWorker>(logger, EnvironmentVariables.ReplicasSynchronisationWorkerDelayMilliseconds, delay);
    private readonly int _slimDataPort = EnvironmentVariables.ReadInteger<SlimDataSynchronizationWorker>(logger, EnvironmentVariables.SlimDataPort, EnvironmentVariables.SlimDataPortDefault);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                await Task.Delay(_delay, stoppingToken);
                // Start SlimData only when 2 replicas are in ready state

                var leadershipToken = cluster.LeadershipToken;
                if (!leadershipToken.IsCancellationRequested)
                {
                    foreach (var slimFaasPod in replicasService.Deployments.SlimFaas.Pods.Where(p => p.Started == true))
                    {
                        string url = $"http://{slimFaasPod.Ip}:3262/";
                        if (cluster.Members.ToList().Any(m => m.EndPoint.ToString() == url) != false)
                        {
                            continue;
                        }
                        logger.LogInformation("SlimFaas pod {PodName} has to be added in the cluster", slimFaasPod.Name);
                        await ((IRaftHttpCluster)cluster).AddMemberAsync(new Uri(url),stoppingToken);
                    }

                    foreach (IRaftClusterMember raftClusterMember in cluster.Members)
                    {
                        if (replicasService.Deployments.SlimFaas.Pods.ToList().Any(slimFaasPod => $"http://{slimFaasPod.Ip}:{_slimDataPort}/" == raftClusterMember.EndPoint.ToString()))
                        {
                            continue;
                        }
                        logger.LogInformation("SlimFaas pod {PodName} need to be remove from the cluster", raftClusterMember.EndPoint.ToString());
                        await ((IRaftHttpCluster)cluster).RemoveMemberAsync( new Uri(raftClusterMember.EndPoint.ToString() ?? string.Empty) ,stoppingToken);
                    }

                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Global Error in SlimDataSynchronizationWorker");
            }
        }
}
}
