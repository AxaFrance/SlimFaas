﻿using DotNext;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.Http;
using Microsoft.AspNetCore.Connections;
using SlimData.Commands;

namespace SlimData;

public class Startup(IConfiguration configuration)
{
    private static readonly IList<string> ClusterMembers = new List<string>(2);

    public static void AddClusterMemberBeforeStart(string endpoint)
    {
        ClusterMembers.Add(endpoint);
    }

    public void Configure(IApplicationBuilder app)
    {
        const string LeaderResource = "/SlimData/leader";
        const string AddHashSetResource = "/SlimData/AddHashset";
        const string ListRightPopResource = "/SlimData/ListRightPop";
        const string ListLeftPushResource = "/SlimData/ListLeftPush";
        const string AddKeyValueResource = "/SlimData/AddKeyValue";
        const string ListLengthResource = "/SlimData/ListLength";
        const string ListSetQueueItemStatus = "/SlimData/ListCallback";
        const string HealthResource = "/health";

        app.UseConsensusProtocolHandler()
            .RedirectToLeader(LeaderResource)
            .RedirectToLeader(ListLengthResource)
            .RedirectToLeader(ListLeftPushResource)
            .RedirectToLeader(ListRightPopResource)
            .RedirectToLeader(AddKeyValueResource)
            .RedirectToLeader(AddHashSetResource)
            .RedirectToLeader(ListSetQueueItemStatus)
            .UseRouting()
            .UseEndpoints(static endpoints =>
            {
                endpoints.MapGet(LeaderResource, Endpoints.RedirectToLeaderAsync);
                endpoints.MapGet(HealthResource, async context => { await context.Response.WriteAsync("OK"); });
                endpoints.MapPost(ListLeftPushResource,  Endpoints.ListLeftPushAsync);
                endpoints.MapPost(ListRightPopResource,  Endpoints.ListRightPopAsync);
                endpoints.MapPost(AddHashSetResource,  Endpoints.AddHashSetAsync);
                endpoints.MapPost(AddKeyValueResource,  Endpoints.AddKeyValueAsync);
                endpoints.MapPost(ListSetQueueItemStatus,  Endpoints.ListCallbackAsync);
            });
    }


    public void ConfigureServices(IServiceCollection services)
    {
        services.UseInMemoryConfigurationStorage(AddClusterMembers)
            .ConfigureCluster<ClusterConfigurator>()
            .AddSingleton<IHttpMessageHandlerFactory, RaftClientHandlerFactory>()
            .AddOptions()
            .AddRouting();
        var path = configuration[SlimPersistentState.LogLocation];
        if (!string.IsNullOrWhiteSpace(path))
            services.UsePersistenceEngine<ISupplier<SlimDataPayload>, SlimPersistentState>();
        var endpoint = configuration["publicEndPoint"];
        if (!string.IsNullOrEmpty(endpoint))
        {
            var uri = new Uri(endpoint);
            services.AddSingleton<SlimDataInfo>(sp => new SlimDataInfo(uri.Port));
        }
    }

    private static void AddClusterMembers(ICollection<UriEndPoint> members)
    {
        foreach (var clusterMember in ClusterMembers)
            members.Add(new UriEndPoint(new Uri(clusterMember, UriKind.Absolute)));
    }
}