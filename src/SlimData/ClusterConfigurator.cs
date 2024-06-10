using System.Diagnostics;
using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft;

namespace SlimData;

internal sealed class ClusterConfigurator : IClusterMemberLifetime
{
    public void OnStart(IRaftCluster cluster, IDictionary<string, string> metadata)
    {
        cluster.LeaderChanged += LeaderChanged;
    }

    public void OnStop(IRaftCluster cluster)
    {
        cluster.LeaderChanged -= LeaderChanged;
    }

    private static void LeaderChanged(ICluster cluster, IClusterMember? leader)
    {
        var now = DateTime.Now;    
        var term = ((IRaftCluster)cluster).Term;
        var timeout = ((IRaftCluster)cluster).ElectionTimeout;
        Console.WriteLine(leader is null
            ? $"{now}: Consensus cannot be reached"
            : $"{now}: New cluster leader is elected. Leader address is {leader.EndPoint}");
        Console.WriteLine($"{now}: Term of local cluster member is {term}. Election timeout {timeout}");
    }
}