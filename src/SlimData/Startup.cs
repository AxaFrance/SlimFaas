using System.Text.Json;
using DotNext;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.Http;
using Microsoft.AspNetCore.Connections;

namespace RaftNode;

public sealed class Startup(IConfiguration configuration)
{
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
        await context.Response.WriteAsync(  JsonSerializer.Serialize(provider.Invoke()), context.RequestAborted);
    }

    public void Configure(IApplicationBuilder app, int slimdataPort=3262)
    {
        const string LeaderResource = "/SlimData/leader";
        const string ValueResource = "/SlimData/value";
        const string AddHashSetResource = "/SlimData/AddHashset";
        const string ListRightPopResource = "/SlimData/ListRightPop";
        const string ListLeftPushResource = "/SlimData/ListLeftPush";
        const string AddKeyValueResource = "/SlimData/AddKeyValue";
        const string ListLengthResource = "/SlimData/ListLength";

        app.UseConsensusProtocolHandler()
            .RedirectToLeader(LeaderResource)
            .RedirectToLeader(ListLengthResource)
            .RedirectToLeader(ListLeftPushResource)
            .RedirectToLeader(ListRightPopResource)
            .RedirectToLeader(AddKeyValueResource)
            .RedirectToLeader(AddHashSetResource)
            .UseRouting()
            .UseEndpoints(static endpoints =>
            {
                endpoints.MapGet(LeaderResource, RedirectToLeaderAsync);
                endpoints.MapGet("/SlimData/health", (async context =>
                {
                    await context.Response.WriteAsync("OK");
                }));
                endpoints.MapGet(ValueResource, GetValueAsync);
                endpoints.MapPost(ListLeftPushResource, async context =>
                {
                    var slimDataInfo = context.RequestServices.GetRequiredService<SlimDataInfo>();
                    if(context.Request.Host.Port != slimDataInfo.Port)
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }
                    var cluster = context.RequestServices.GetRequiredService<IRaftCluster>();
                    var provider = context.RequestServices.GetRequiredService<SlimPersistentState>();
                    var source =
                        CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,
                            cluster.LeadershipToken);
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

                        var logEntry =
                            provider.interpreter.CreateLogEntry(new ListLeftPushCommand() { Key = key, Value = value },
                                cluster.Term);
                        await provider.AppendAsync(logEntry, source.Token);
                        await provider.CommitAsync(source.Token);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Unexpected error {0}", e);
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    }
                    finally
                    {
                        source?.Dispose();
                    }
                });
                
                endpoints.MapPost(ListRightPopResource, async context =>
                {
                    var slimDataInfo = context.RequestServices.GetRequiredService<SlimDataInfo>();
                    if(context.Request.Host.Port != slimDataInfo.Port)
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }
                    var cluster = context.RequestServices.GetRequiredService<IRaftCluster>();
                    var provider = context.RequestServices.GetRequiredService<SlimPersistentState>();
                    var source =
                        CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,
                            cluster.LeadershipToken);
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

                        if (string.IsNullOrEmpty(key) || !int.TryParse(value, out int count))
                        {
                            context.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await context.Response.WriteAsync("Key key is empty or value is not a number",
                                context.RequestAborted);
                            return;
                        }

                        await cluster.ApplyReadBarrierAsync(context.RequestAborted);

                        var values = new List<string>();
                        var queues = ((ISupplier<SupplierPayload>)provider).Invoke().Queues;
                        if (queues.ContainsKey(key))
                        {
                            var queue = ((ISupplier<SupplierPayload>)provider).Invoke().Queues[key];
                            for (var i = 0; i < count; i++)
                            {
                                if (queue.Count <= i)
                                {
                                    break;
                                }

                                values.Add(queue[i]);
                            }

                            await context.Response.WriteAsync(JsonSerializer.Serialize(values), context.RequestAborted);
                            var logEntry =
                                provider.interpreter.CreateLogEntry(new ListRightPopCommand() { Key = key, Count = count },
                                    cluster.Term);
                            await provider.AppendAsync(logEntry, source.Token);
                            await provider.CommitAsync(source.Token);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Unexpected error {0}", e);
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    }
                    finally
                    {
                        source?.Dispose();
                    }
                });
                endpoints.MapPost(AddHashSetResource, async context =>
                {
                    var slimDataInfo = context.RequestServices.GetRequiredService<SlimDataInfo>();
                    if(context.Request.Host.Port != slimDataInfo.Port)
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }
                    var cluster = context.RequestServices.GetRequiredService<IRaftCluster>();
                    var provider = context.RequestServices.GetRequiredService<SlimPersistentState>();
                    var source =
                        CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,
                            cluster.LeadershipToken);
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

                        var logEntry =
                            provider.interpreter.CreateLogEntry(
                                new AddHashSetCommand() { Key = key, Value = dictionary }, cluster.Term);
                        await provider.AppendAsync(logEntry, source.Token);
                        await provider.CommitAsync(source.Token);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Unexpected error {0}", e);
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    }
                    finally
                    {
                        source?.Dispose();
                    }
                });
                endpoints.MapPost(AddKeyValueResource, async context =>
                {
                    var slimDataInfo = context.RequestServices.GetRequiredService<SlimDataInfo>();
                    if(context.Request.Host.Port != slimDataInfo.Port)
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }
                    var cluster = context.RequestServices.GetRequiredService<IRaftCluster>();
                    var provider = context.RequestServices.GetRequiredService<SlimPersistentState>();
                    var source =
                        CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,
                            cluster.LeadershipToken);
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

                        var logEntry =
                            provider.interpreter.CreateLogEntry(new AddKeyValueCommand() { Key = key, Value = value },
                                cluster.Term);
                        await provider.AppendAsync(logEntry, source.Token);
                        await provider.CommitAsync(source.Token);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Unexpected error {0}", e);
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    }
                    finally
                    {
                        source?.Dispose();
                    }
                });
            });
    }

    public void ConfigureServices(IServiceCollection services, int slimDataPort=3262)
    {
        services.UseInMemoryConfigurationStorage(AddClusterMembers)
            .ConfigureCluster<ClusterConfigurator>()
            .AddSingleton<IHttpMessageHandlerFactory, RaftClientHandlerFactory>()
            .AddOptions()
            .AddRouting();
        services.AddSingleton<SlimDataInfo>(sp => new SlimDataInfo(slimDataPort));
        var path = configuration[SlimPersistentState.LogLocation];
        if (!string.IsNullOrWhiteSpace(path))
        {
            services.UsePersistenceEngine<ISupplier<SupplierPayload>, SlimPersistentState>();
        }
    }

    public static readonly IList<string> ClusterMembers = new List<string>(2); 
    
    // NOTE: this way of adding members to the cluster is not recommended in production code
    private static void AddClusterMembers(ICollection<UriEndPoint> members)
    {
        foreach (var clusterMember in ClusterMembers)
        {
            members.Add(new UriEndPoint(new(clusterMember, UriKind.Absolute)));
        }
        //members.Add(new UriEndPoint(new("http://localhost:3262", UriKind.Absolute)));
        //members.Add(new UriEndPoint(new("http://localhost:3263", UriKind.Absolute)));
        //members.Add(new UriEndPoint(new("http://localhost:3264", UriKind.Absolute)));
    }
}