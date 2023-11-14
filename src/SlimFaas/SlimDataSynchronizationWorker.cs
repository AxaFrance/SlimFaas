using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.Http;
using RaftNode;

namespace SlimFaas;

public class SlimDataSynchronizationWorker: BackgroundService
{
    private readonly IReplicasService _replicasService;
    private readonly IRaftCluster _cluster;
    private readonly ILogger<SlimDataSynchronizationWorker> _logger;
    private readonly int _delay;
    private bool _isStarted = false;

    public SlimDataSynchronizationWorker(IReplicasService replicasService, IRaftCluster cluster, ILogger<SlimDataSynchronizationWorker> logger, int delay = EnvironmentVariables.ReplicasSynchronizationWorkerDelayMillisecondsDefault)
    {
        _replicasService = replicasService;
        _cluster = cluster;
        _logger = logger;
        _delay = EnvironmentVariables.ReadInteger(logger, EnvironmentVariables.ReplicasSynchronisationWorkerDelayMilliseconds, delay);

    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                await Task.Delay(_delay, stoppingToken);
                // Start SlimData only when 2 replicas are in ready state

                var leadershipToken = _cluster.LeadershipToken;
                if (!leadershipToken.IsCancellationRequested)
                {
                    foreach (var slimFaasPod in _replicasService.Deployments.SlimFaas.Pods)
                    {
                        string url = $"http://{slimFaasPod.Ip}:3262/";
                        if (_cluster.Members.ToList().Any(m => m.EndPoint.ToString() == url) != false)
                        {
                            continue;
                        }
                        _logger.LogInformation("SlimFaas pod {PodName} has to be added in the cluster", slimFaasPod.Name);
                        await ((IRaftHttpCluster)_cluster).AddMemberAsync(new Uri(url),stoppingToken);
                    }

                    foreach (IRaftClusterMember raftClusterMember in _cluster.Members)
                    {
                        var slimDataPort = int.Parse( Environment.GetEnvironmentVariable("SLIMDATA_PORT") ?? "3262");
                        if (_replicasService.Deployments.SlimFaas.Pods.ToList().Any(slimFaasPod => $"http://{slimFaasPod.Ip}:{slimDataPort}/" == raftClusterMember.EndPoint.ToString()))
                        {
                            continue;
                        }
                        _logger.LogInformation("SlimFaas pod {PodName} need to be remove from the cluster", raftClusterMember.EndPoint.ToString());
                        await ((IRaftHttpCluster)_cluster).RemoveMemberAsync( new Uri(raftClusterMember.EndPoint.ToString() ?? string.Empty) ,stoppingToken);
                    }

                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Global Error in SlimDataSynchronizationWorker");
            }
        }
}
}
