using DotNext.Net.Cluster.Consensus.Raft;
using k8s.KubeConfigModels;

namespace SlimFaas;

public interface ISlimDataStatus
{
    Task WaitForReadyAsync();
}

public class SlimDataMock() : ISlimDataStatus
{
    public async Task WaitForReadyAsync()
    {
        await Task.CompletedTask;
    }
}

public class SlimDataStatus(IRaftCluster cluster) : ISlimDataStatus
{
    public async Task WaitForReadyAsync()
    {
        var raftCluster = cluster;

        while (raftCluster.Leader == null)
        {
            Console.WriteLine($"Raft cluster has no leader");
            await Task.Delay(500);
        }
    }

}
