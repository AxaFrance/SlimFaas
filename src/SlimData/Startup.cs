using System.Text.Json.Serialization;
using DotNext;
using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.Http;
using DotNext.Net.Cluster.Consensus.Raft.Membership;
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
        const string ListLengthResource = "/ListLength";

        app.UseConsensusProtocolHandler()
            .RedirectToLeader(LeaderResource)
            .UseRouting()
            .UseEndpoints(static endpoints =>
            {
                endpoints.MapGet(LeaderResource, RedirectToLeaderAsync);
                endpoints.MapGet(ValueResource, GetValueAsync);
                endpoints.MapGet(ListLengthResource, async context =>
                {
                    var cluster = context.RequestServices.GetRequiredService<IRaftCluster>();
                    var provider = context.RequestServices.GetRequiredService<SimplePersistentState>();
                    var source = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, cluster.LeadershipToken);
                    try
                    {
                        context.Request.Query.TryGetValue("key", out var key);

                        if (string.IsNullOrEmpty(key))
                        {
                            await context.Response.WriteAsync("0", context.RequestAborted);
                            return;
                        }
                        
                        await cluster.ApplyReadBarrierAsync(context.RequestAborted);
                        var queue = ((ISupplier<SupplierPayload>)provider).Invoke().Queues;
                        var queueCount =  queue.ContainsKey(key) ?  queue[key].Count : 0;
                        await context.Response.WriteAsync(queueCount.ToString(InvariantCulture), context.RequestAborted); 
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
                endpoints.MapPost(ListLeftPushResource, async context =>
                {
                    var cluster = context.RequestServices.GetRequiredService<IRaftCluster>();
                    var provider = context.RequestServices.GetRequiredService<SimplePersistentState>();
                    var source = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, cluster.LeadershipToken);
                    try
                    {
                        var form = await context.Request.ReadFormAsync(source.Token);

                        var key = string.Empty;
                        var value = string.Empty;
                        foreach (var formData in form)
                        {
                                key = formData.Key;
                                value = formData.Value.ToString();
                                break;
                        }

                        if (string.IsNullOrEmpty(key))
                        {
                            context.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await context.Response.WriteAsync("Key key is empty", context.RequestAborted);
                            return;
                        }
                        
                        var logEntry = provider.interpreter.CreateLogEntry(new ListLeftPushCommand() { Key = key, Value = value}, cluster.Term); 
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
                endpoints.MapPost(ListRightPopResource, async context =>
                {
                    var cluster = context.RequestServices.GetRequiredService<IRaftCluster>();
                    var provider = context.RequestServices.GetRequiredService<SimplePersistentState>();
                    var source = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, cluster.LeadershipToken);
                    try
                    {
                        var form = await context.Request.ReadFormAsync(source.Token);

                        var key = string.Empty;
                        var value = string.Empty;
                        foreach (var formData in form)
                        {
                            key = formData.Key;
                            value = formData.Value.ToString();
                            break;
                        }

                        if (string.IsNullOrEmpty(key) || int.TryParse(value, out int count))
                        {
                            context.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await context.Response.WriteAsync("Key key is empty or value is not a number", context.RequestAborted);
                            return;
                        }
                        
                        await cluster.ApplyReadBarrierAsync(context.RequestAborted);

                        IList<string> values = new List<string>();
                        var queue = ((ISupplier<SupplierPayload>)provider).Invoke().Queues[key];
                        for (var i = 0; i < count; i++)
                        {
                            if (queue.Count <= i)
                            {
                                break;
                            }
                            values.Add(queue[i]);
                        }
                        await context.Response.WriteAsync(JsonConvert.SerializeObject(values), context.RequestAborted); 
                        var logEntry = provider.interpreter.CreateLogEntry(new ListRightPopCommand() { Key = key, Count = count}, cluster.Term); 
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
                                dictionary[formData.Key] = formData.Value.ToString();
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
                endpoints.MapPost(AddKeyValueResource, async context =>
                {
                    var cluster = context.RequestServices.GetRequiredService<IRaftCluster>();
                    var provider = context.RequestServices.GetRequiredService<SimplePersistentState>();
                    var source = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, cluster.LeadershipToken);
                    try
                    {
                        var form = await context.Request.ReadFormAsync(source.Token);

                        var key = string.Empty;
                        var value = string.Empty;

                        foreach (var keyValue in form)
                        {
                            key = keyValue.Key;
                            value = keyValue.Value.ToString();
                            break;
                        }

                        if (string.IsNullOrEmpty(key))
                        {
                            context.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await context.Response.WriteAsync("not data found", context.RequestAborted);
                            return;
                        }
                        
                        var logEntry = provider.interpreter.CreateLogEntry(new AddKeyValueCommand() { Key = key, Value = value}, cluster.Term); 
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
                endpoints.MapPost("AddMember", async context =>
                {
                    var cluster = context.RequestServices.GetRequiredService< IRaftCluster>();
                    
                    var source = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, cluster.LeadershipToken);
                    try
                    {
                        var form = await context.Request.ReadFormAsync(source.Token);

                        var key = string.Empty;
                        var value = string.Empty;
                        foreach (var formData in form)
                        {
                            value = formData.Value.ToString();
                            if (cluster.Members
                                    .Count(m => m.EndPoint == new UriEndPoint(new(value, UriKind.Absolute))) == 0)
                            {
                                await ((RaftCluster)cluster).AddMemberAsync(new UriEndPoint(new(value, UriKind.Absolute)), context.RequestAborted);
                            }
                            break;
                        }

                        await context.Response.WriteAsync("", context.RequestAborted); 

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