﻿using DotNext.Net.Cluster.Consensus.Raft;

namespace SlimFaas;

public interface IMasterService
{
    bool IsMaster { get; }
}

public class MasterSlimDataService(IRaftCluster cluster) : IMasterService
{
    public bool IsMaster { get
        {

            var leadershipToken = cluster.LeadershipToken;
            return !leadershipToken.IsCancellationRequested;
        }}

    public Task CheckAsync()
    {
        return Task.CompletedTask;
    }

}
