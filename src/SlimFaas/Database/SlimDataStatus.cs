using DotNext.Net.Cluster.Consensus.Raft;
using k8s.KubeConfigModels;

namespace SlimFaas;

public class SlimDataStatus
{
    private readonly IRaftCluster _cluster;

    public SlimDataStatus(IRaftCluster cluster)
    {
        _cluster = cluster;
    }

    public async Task WaitForReadyAsync()
    {
        var raftCluster = _cluster;
        while (raftCluster.Readiness != Task.CompletedTask)
        {
            Console.WriteLine($"Raft cluster is not ready");
            await Task.Delay(500);
        }

        while (raftCluster.Leader == null)
        {
            Console.WriteLine($"Raft cluster has no leader");
            await Task.Delay(500);
        }
    }

}
