using System.Text.Json.Serialization;
using DotNext;
using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.Http;
using Microsoft.AspNetCore.Connections;
using Newtonsoft.Json;
using static System.Globalization.CultureInfo;

namespace RaftNode;

internal sealed class Startup
{
    private readonly IConfiguration configuration;

    public Startup(IConfiguration configuration) => this.configuration = configuration;

    private static Task RedirectToLeaderAsync(HttpContext context)
    {
        var cluster = context.RequestServices.GetRequiredService<IRaftCluster>();
        return context.Response.WriteAsync($"Leader address is {cluster.Leader?.EndPoint}. Current address is {context.Connection.LocalIpAddress}:{context.Connection.LocalPort}", context.RequestAborted);
    }

    private static async Task GetValueAsync(HttpContext context)
    {
        var cluster = context.RequestServices.GetRequiredService<IRaftCluster>();
        var provider = context.RequestServices.GetRequiredService<ISupplier<SupplierPayload>>();

        await cluster.ApplyReadBarrierAsync(context.RequestAborted);
        await context.Response.WriteAsync(  JsonConvert.SerializeObject(provider.Invoke()), context.RequestAborted);
    }

    public void Configure(IApplicationBuilder app)
    {
        const string LeaderResource = "/leader";
        const string ValueResource = "/value";
        const string AddHashSetResource = "/AddHashset";
        const string ListRightPopResource = "/ListRightPop";
        const string ListLeftPushResource = "/ListLeftPush";
        const string AddKeyValueResource = "/AddKeyValue";

        app.UseConsensusProtocolHandler()
            .RedirectToLeader(LeaderResource)
            .UseRouting()
            .UseEndpoints(static endpoints =>
            {
                endpoints.MapGet(LeaderResource, RedirectToLeaderAsync);
                endpoints.MapGet(ValueResource, GetValueAsync);
                endpoints.MapPost(AddHashSetResource, async context =>
                {
                    var cluster = context.RequestServices.GetRequiredService<IRaftCluster>();
                    var provider = context.RequestServices.GetRequiredService<SimplePersistentState>();
                    var source = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, cluster.LeadershipToken);
                    try
                    {
                        var form = await context.Request.ReadFormAsync(source.Token);

                        var key = string.Empty;
                        var dictionary = new Dictionary<string, string>();
                        foreach (var formData in form)
                        {
                            if (formData.Key == "______key_____")
                            {
                                key = formData.Value.ToString();
                            }
                            else
                            {
                                dictionary.TryAdd(formData.Key, formData.Value.ToString());
                            }
                        }

                        if (string.IsNullOrEmpty(key))
                        {
                            context.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await context.Response.WriteAsync("Key ______key_____ is empty", context.RequestAborted);
                            return;
                        }
                        
                        var logEntry = provider.interpreter.CreateLogEntry(new AddHashSetCommand() { Key = key, Value = dictionary}, cluster.Term); 
                        await provider.AppendAsync(logEntry, source.Token);
                        await provider.CommitAsync(source.Token);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Unexpected error {0}", e);
                    }
                    finally
                    {
                        source?.Dispose();
                    }
                });
            });
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.UseInMemoryConfigurationStorage(AddClusterMembers)
            .ConfigureCluster<ClusterConfigurator>()
            .AddSingleton<IHttpMessageHandlerFactory, RaftClientHandlerFactory>()
            .AddOptions()
            .AddRouting();

        var path = configuration[SimplePersistentState.LogLocation];
        if (!string.IsNullOrWhiteSpace(path))
        {
            services.UsePersistenceEngine<ISupplier<SupplierPayload>, SimplePersistentState>()
                .AddSingleton<IHostedService, DataModifier>();
        }
    }

    // NOTE: this way of adding members to the cluster is not recommended in production code
    private static void AddClusterMembers(ICollection<UriEndPoint> members)
    {
        members.Add(new UriEndPoint(new("http://localhost:3262", UriKind.Absolute)));
        members.Add(new UriEndPoint(new("http://localhost:3263", UriKind.Absolute)));
        members.Add(new UriEndPoint(new("http://localhost:3264", UriKind.Absolute)));
    }
}