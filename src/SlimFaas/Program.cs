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
var slimDataDirectory = Environment.GetEnvironmentVariable("SLIMDATA_DIRECTORY") ?? "c://Demo4";

var serviceCollectionStarter = new ServiceCollection();
serviceCollectionStarter.AddSingleton<IReplicasService, ReplicasService>();
serviceCollectionStarter.AddSingleton<HistoryHttpMemoryService, HistoryHttpMemoryService>();

var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
var configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{environment} .json", true)
    .AddEnvironmentVariables().Build();

var mockKubernetesFunction = Environment.GetEnvironmentVariable(EnvironmentVariables.MockKubernetesFunctions);
if (!string.IsNullOrEmpty(mockKubernetesFunction))
{
    serviceCollectionStarter.AddSingleton<IKubernetesService, MockKubernetesService>();
}
else
{
    serviceCollectionStarter.AddSingleton<IKubernetesService, KubernetesService>(sp =>
    {
        var useKubeConfig = bool.Parse(configuration["UseKubeConfig"] ?? "false");
        return new KubernetesService(sp.GetRequiredService<ILogger<KubernetesService>>(), useKubeConfig);
    });
}

serviceCollectionStarter.AddLogging(loggingBuilder =>
{
    loggingBuilder.AddConsole();
    loggingBuilder.AddDebug();
});

var serviceProviderStarter = serviceCollectionStarter.BuildServiceProvider();

var replicasService = serviceProviderStarter.GetService<IReplicasService>();
string namespace_ = Environment.GetEnvironmentVariable(EnvironmentVariables.Namespace) ?? EnvironmentVariables.NamespaceDefault;
Console.WriteLine($"Starting in namespace {namespace_}");
replicasService?.SyncDeploymentsAsync(namespace_).Wait();

while (replicasService?.Deployments.SlimFaas.Pods.Count <= 2)
{
    Console.WriteLine("Waiting for pods to be ready");
    Thread.Sleep(1000);
    replicasService?.SyncDeploymentsAsync(namespace_).Wait();
}

if (replicasService?.Deployments?.SlimFaas?.Pods != null)
{
    foreach (string enumerateDirectory in Directory.EnumerateDirectories(slimDataDirectory))
    {
        if (replicasService.Deployments.SlimFaas.Pods.Any(p => p.Name == new DirectoryInfo(enumerateDirectory).Name) == false)
        {
            try
            {
                Console.WriteLine($"Deleting {enumerateDirectory}");
                Directory.Delete(enumerateDirectory, false);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }

    foreach (PodInformation podInformation in replicasService.Deployments.SlimFaas.Pods)
    {
        string item = $"http://{podInformation.Ip}:{(string.IsNullOrEmpty(podInformation.Port) ? "3262" : podInformation.Port)}";
        Console.WriteLine($"Adding node  {item}");
        Startup.ClusterMembers.Add(item);
    }

    var currentPod = replicasService.Deployments.SlimFaas.Pods.First(p => p.Name == Environment.GetEnvironmentVariable("HOSTNAME"));
    var podDataDirectory =  Path.Combine(slimDataDirectory, currentPod.Name);
    if(Directory.Exists(podDataDirectory) == false)
        Directory.CreateDirectory(podDataDirectory);
    Starter.StartNode("http", slimDataPort, podDataDirectory);
}

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
serviceCollection.AddHostedService<SlimDataSynchronizationWorker>();
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

serviceCollection.AddSingleton<IRaftCluster, IRaftCluster>((sp) => raftCluster);
serviceCollection.AddSingleton<SimplePersistentState, SimplePersistentState>((sp) => Starter.ServiceProvider.GetRequiredService<SimplePersistentState>());

#pragma warning restore CA2252
var mockRedis = Environment.GetEnvironmentVariable(EnvironmentVariables.MockRedis);
if (!string.IsNullOrEmpty(mockRedis))
{
      serviceCollection.AddSingleton<IRedisService, RedisMockService>();
}
else
{
    serviceCollection.AddSingleton<IRedisService, SlimDataService>();
    serviceCollection.AddHttpClient<IRedisService, SlimDataService>()
        .SetHandlerLifetime(TimeSpan.FromMinutes(5)).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
        {
            AllowAutoRedirect = true
        });
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


