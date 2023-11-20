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

var slimDataDirectory = Environment.GetEnvironmentVariable(EnvironmentVariables.SlimDataDirectory) ?? EnvironmentVariables.SlimDataDirectoryDefault;
var mockSlimData = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvironmentVariables.MockSlimData));

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


var builder = WebApplication.CreateBuilder(args);

var serviceCollectionSlimFaas = builder.Services;
serviceCollectionSlimFaas.AddHostedService<SlimWorker>();
serviceCollectionSlimFaas.AddHostedService<ScaleReplicasWorker>();
serviceCollectionSlimFaas.AddHostedService<ReplicasSynchronizationWorker>();
serviceCollectionSlimFaas.AddHostedService<HistorySynchronizationWorker>();
serviceCollectionSlimFaas.AddHttpClient();
serviceCollectionSlimFaas.AddSingleton<ISlimFaasQueue, SlimFaasSlimFaasQueue>();
serviceCollectionSlimFaas.AddSingleton<IReplicasService, ReplicasService>((sp) => (ReplicasService)serviceProviderStarter.GetService<IReplicasService>()!);
serviceCollectionSlimFaas.AddSingleton<HistoryHttpRedisService, HistoryHttpRedisService>();
serviceCollectionSlimFaas.AddSingleton<HistoryHttpMemoryService, HistoryHttpMemoryService>((sp) => serviceProviderStarter.GetService<HistoryHttpMemoryService>()!);

if (!string.IsNullOrEmpty(mockKubernetesFunction))
{
    serviceCollectionSlimFaas.AddSingleton<IKubernetesService, MockKubernetesService>((sp) => (MockKubernetesService)serviceProviderStarter.GetService<IKubernetesService>()!);
}
else
{
    serviceCollectionSlimFaas.AddSingleton<IKubernetesService, KubernetesService>((sp) => (KubernetesService)serviceProviderStarter.GetService<IKubernetesService>()!);
}


if (mockSlimData == false)
{
    string namespace_ = Environment.GetEnvironmentVariable(EnvironmentVariables.Namespace) ??
                        EnvironmentVariables.NamespaceDefault;
    Console.WriteLine($"Starting in namespace {namespace_}");
    replicasService?.SyncDeploymentsAsync(namespace_).Wait();

    var hostname = Environment.GetEnvironmentVariable("HOSTNAME");
    while (replicasService?.Deployments.SlimFaas.Pods.Select(p => !string.IsNullOrEmpty(p.Ip) && p.Started == true).Count() < 3 ||
           replicasService?.Deployments.SlimFaas.Pods.Any(p => p.Name == hostname) == false)
    {
        Console.WriteLine("Waiting for pods to be ready");
        Thread.Sleep(1000);
        replicasService?.SyncDeploymentsAsync(namespace_).Wait();
    }

    if (replicasService?.Deployments?.SlimFaas?.Pods != null)
    {
        foreach (string enumerateDirectory in Directory.EnumerateDirectories(slimDataDirectory))
        {
            if (replicasService.Deployments.SlimFaas.Pods.Any(p =>
                    p.Name == new DirectoryInfo(enumerateDirectory).Name) == false)
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


        foreach (PodInformation podInformation in replicasService.Deployments.SlimFaas.Pods
                     .Where(p => !string.IsNullOrEmpty(p.Ip) && p.Started == true).ToList())
        {
            string slimDataEndpoint = SlimDataEndpoint(podInformation);
            Console.WriteLine($"Adding node  {slimDataEndpoint}");
            Startup.ClusterMembers.Add(slimDataEndpoint);
        }

        var currentPod = replicasService.Deployments.SlimFaas.Pods.First(p => p.Name == hostname);
        Console.WriteLine($"Starting node {currentPod.Name}");
        var podDataDirectory = Path.Combine(slimDataDirectory, currentPod.Name);
        if (Directory.Exists(podDataDirectory) == false)
            Directory.CreateDirectory(podDataDirectory);
        Starter.StartNode(SlimDataEndpoint(currentPod), podDataDirectory);
        Console.WriteLine($"Node started {currentPod.Name}");
    }

    while (Starter.ServiceProvider == null)
    {
        Console.WriteLine($"Waiting node to start");
        Thread.Sleep(500);
    }

    var raftCluster = Starter.ServiceProvider.GetRequiredService<IRaftCluster>();
    while (raftCluster.Readiness == Task.CompletedTask)
    {
        Console.WriteLine($"Raft cluster is not ready");
        Thread.Sleep(500);
    }

    while (raftCluster.Leader == null)
    {
        Console.WriteLine($"Raft cluster has no leader");
        Thread.Sleep(500);
    }

    serviceCollectionSlimFaas.AddSingleton<IRaftCluster, IRaftCluster>((sp) => raftCluster);
    serviceCollectionSlimFaas.AddSingleton<SimplePersistentState, SimplePersistentState>((sp) =>
        Starter.ServiceProvider.GetRequiredService<SimplePersistentState>());
    serviceCollectionSlimFaas.AddHostedService<SlimDataSynchronizationWorker>();
    serviceCollectionSlimFaas.AddSingleton<IDatabaseService, SlimDataService>();
    serviceCollectionSlimFaas.AddHttpClient<IDatabaseService, SlimDataService>()
        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler() { AllowAutoRedirect = true });
}
else
{
    serviceCollectionSlimFaas.AddSingleton<IDatabaseService, DatabaseMockService>();
}


serviceCollectionSlimFaas.AddSingleton<IMasterService, MasterSlimDataService>();

serviceCollectionSlimFaas.AddScoped<ISendClient, SendClient>();
serviceCollectionSlimFaas.AddHttpClient<ISendClient, SendClient>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
    .AddPolicyHandler(GetRetryPolicy());
serviceCollectionSlimFaas.AddOpenTelemetry()
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

string SlimDataEndpoint(PodInformation podInformation1)
{
    var s = Environment.GetEnvironmentVariable(EnvironmentVariables.BaseSlimDataUrl) ??
            EnvironmentVariables.BaseFunctionUrlDefault;

    s = s.Replace("{pod_name}", podInformation1.Name);
    s = s.Replace("{pod_ip}", podInformation1.Ip);
    return s;
}

public partial class Program { }


#pragma warning restore CA2252
