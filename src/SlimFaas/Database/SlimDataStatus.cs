using DotNext.Net.Cluster.Consensus.Raft;

namespace SlimFaas.Database;

public interface ISlimDataStatus
{
    Task WaitForReadyAsync();
}

public class SlimDataMock : ISlimDataStatus
{
    public async Task WaitForReadyAsync() => await Task.CompletedTask;
}

public class SlimDataStatus(IRaftCluster cluster) : ISlimDataStatus
{
    public async Task WaitForReadyAsync()
    {
        IRaftCluster raftCluster = cluster;

        while (raftCluster.Leader == null)
        {
            Console.WriteLine("Raft cluster has no leader");
            await Task.Delay(500);
        }
    }
}
