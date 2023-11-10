using System.Net;
using DotNext.Net.Cluster.Consensus.Raft;
using OpenTelemetry.Trace;
using SlimFaas;
using Polly;
using Polly.Extensions.Http;
using Prometheus;
using RaftNode;
using SlimFaas.Kubernetes;

#pragma warning disable CA2252

var slimDataPort = int.Parse( Environment.GetEnvironmentVariable("SLIMDATA_PORT") ?? "3262");
var slimDataDirectory = Environment.GetEnvironmentVariable("SLIMDATA_DIRECTORY") ?? "c://Demo1";

var serviceCollectionStarter = new ServiceCollection();
serviceCollectionStarter.AddSingleton<IReplicasService, ReplicasService>();
serviceCollectionStarter.AddSingleton<HistoryHttpMemoryService, HistoryHttpMemoryService>();

var mockKubernetesFunction = Environment.GetEnvironmentVariable(EnvironmentVariables.MockKubernetesFunctions);
if (!string.IsNullOrEmpty(mockKubernetesFunction))
{
    serviceCollectionStarter.AddSingleton<IKubernetesService, MockKubernetesService>();
}
else
{
    serviceCollectionStarter.AddSingleton<IKubernetesService, KubernetesService>();
}

serviceCollectionStarter.AddLogging(loggingBuilder =>
{
    loggingBuilder.AddConsole();
    loggingBuilder.AddDebug();
});

var serviceProviderStarter = serviceCollectionStarter.BuildServiceProvider();

    var replicasService = serviceProviderStarter.GetService<IReplicasService>();
    string namespace_ =
    Environment.GetEnvironmentVariable(EnvironmentVariables.Namespace) ?? EnvironmentVariables.NamespaceDefault;
    replicasService?.SyncDeploymentsAsync(namespace_).Wait();


    if (replicasService?.Deployments?.SlimFaas?.Pods != null)
    {
        foreach (PodInformation podInformation in replicasService.Deployments.SlimFaas.Pods)
        {
            Startup.ClusterMembers.Add(
                $"http://{podInformation.Ip}:{(string.IsNullOrEmpty(podInformation.Port) ? "3262" : podInformation.Port)}");
        }
    }

    Starter.StartNode("http", slimDataPort, slimDataDirectory);

    while (Starter.ServiceProvider == null)
    {
        Thread.Sleep(100);
    }

    var raftCluster = Starter.ServiceProvider.GetRequiredService<IRaftCluster>();
    while (raftCluster.Readiness == Task.CompletedTask)
    {
        Thread.Sleep(100);
    }

    while (raftCluster.Leader == null)
    {
        Thread.Sleep(100);
    }

    var builder = WebApplication.CreateBuilder(args);

    var serviceCollection = builder.Services;
    serviceCollection.AddHostedService<SlimWorker>();
    serviceCollection.AddHostedService<ScaleReplicasWorker>();
    serviceCollection.AddHostedService<MasterWorker>();
    serviceCollection.AddHostedService<ReplicasSynchronizationWorker>();
    serviceCollection.AddHostedService<HistorySynchronizationWorker>();
    serviceCollection.AddHttpClient();
    serviceCollection.AddSingleton<IQueue, RedisQueue>();

serviceCollection.AddSingleton<IReplicasService, ReplicasService>((sp) => (ReplicasService)serviceProviderStarter.GetService<IReplicasService>()!);
serviceCollection.AddSingleton<HistoryHttpRedisService, HistoryHttpRedisService>();
serviceCollection.AddSingleton<HistoryHttpMemoryService, HistoryHttpMemoryService>((sp) => serviceProviderStarter.GetService<HistoryHttpMemoryService>()!);

if (!string.IsNullOrEmpty(mockKubernetesFunction))
{
    serviceCollection.AddSingleton<IKubernetesService, MockKubernetesService>((sp) => (MockKubernetesService)serviceProviderStarter.GetService<IKubernetesService>()!);
}
else
{
    serviceCollection.AddSingleton<IKubernetesService, KubernetesService>((sp) => (KubernetesService)serviceProviderStarter.GetService<IKubernetesService>()!);
}

serviceCollection.AddSingleton<IRaftCluster, IRaftCluster>((sp) => Starter.ServiceProvider.GetRequiredService<IRaftCluster>());
serviceCollection.AddSingleton<SimplePersistentState, SimplePersistentState>((sp) => Starter.ServiceProvider.GetRequiredService<SimplePersistentState>());

#pragma warning restore CA2252
var mockRedis = Environment.GetEnvironmentVariable(EnvironmentVariables.MockRedis);
//if (!string.IsNullOrEmpty(mockRedis))
{
    //  serviceCollection.AddSingleton<IRedisService, RedisMockService>();
}
//else
{
    serviceCollection.AddSingleton<IRedisService, SlimDataService>();
    serviceCollection.AddHttpClient<IRedisService, SlimDataService>()
        .SetHandlerLifetime(TimeSpan.FromMinutes(5)).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
        {
            AllowAutoRedirect = true
        });;
}
serviceCollection.AddSingleton<IMasterService, MasterService>();

serviceCollection.AddScoped<ISendClient, SendClient>();
serviceCollection.AddHttpClient<ISendClient, SendClient>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
    .AddPolicyHandler(GetRetryPolicy());
serviceCollection.AddOpenTelemetry()
    .WithTracing(builder => builder
        .AddHttpClientInstrumentation()
        .AddAspNetCoreInstrumentation());

var app = builder.Build();


app.Use(async (context, next) =>
{
    if (context.Request.Path == "/health")
    {
        await context.Response.WriteAsync("OK");
        return;
    }
    await next.Invoke();
});

app.UseMetricServer();
app.UseHttpMetrics();
app.UseMiddleware<SlimProxyMiddleware>();

app.Run(context =>
{
    context.Response.StatusCode = 404;
    return Task.CompletedTask;
});

app.Run();

serviceProviderStarter.Dispose();
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg =>
            {
                HttpStatusCode[] httpStatusCodesWorthRetrying = {
                    HttpStatusCode.RequestTimeout, // 408
                    HttpStatusCode.InternalServerError, // 500
                    HttpStatusCode.BadGateway, // 502
                    HttpStatusCode.ServiceUnavailable, // 503
                    HttpStatusCode.GatewayTimeout // 504
                };
                return httpStatusCodesWorthRetrying.Contains(msg.StatusCode);
            })
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2,
                retryAttempt)));
    }

    public partial class Program { }


