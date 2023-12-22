using System.Net;
using DotNext.Net.Cluster.Consensus.Raft.Http;
using OpenTelemetry.Trace;
using Polly;
using Polly.Extensions.Http;
using Prometheus;
using RaftNode;
using SlimFaas;
using SlimFaas.Kubernetes;

#pragma warning disable CA2252

string slimDataDirectory = Environment.GetEnvironmentVariable(EnvironmentVariables.SlimDataDirectory) ??
                           EnvironmentVariables.GetTemporaryDirectory();
bool slimDataAllowColdStart =
    bool.Parse(Environment.GetEnvironmentVariable(EnvironmentVariables.SlimDataAllowColdStart) ??
               EnvironmentVariables.SlimDataAllowColdStartDefault.ToString());

ServiceCollection serviceCollectionStarter = new();
serviceCollectionStarter.AddSingleton<IReplicasService, ReplicasService>();
serviceCollectionStarter.AddSingleton<HistoryHttpMemoryService, HistoryHttpMemoryService>();

string? environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
IConfigurationRoot configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{environment} .json", true)
    .AddEnvironmentVariables().Build();

string? mockKubernetesFunction = Environment.GetEnvironmentVariable(EnvironmentVariables.MockKubernetesFunctions);
if (!string.IsNullOrEmpty(mockKubernetesFunction))
{
    serviceCollectionStarter.AddSingleton<IKubernetesService, MockKubernetesService>();
}
else
{
    serviceCollectionStarter.AddSingleton<IKubernetesService, KubernetesService>(sp =>
    {
        bool useKubeConfig = bool.Parse(configuration["UseKubeConfig"] ?? "false");
        return new KubernetesService(sp.GetRequiredService<ILogger<KubernetesService>>(), useKubeConfig);
    });
}

serviceCollectionStarter.AddLogging(loggingBuilder =>
{
    loggingBuilder.AddConsole();
    loggingBuilder.AddDebug();
});

ServiceProvider serviceProviderStarter = serviceCollectionStarter.BuildServiceProvider();
IReplicasService? replicasService = serviceProviderStarter.GetService<IReplicasService>();


WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

IServiceCollection serviceCollectionSlimFaas = builder.Services;
serviceCollectionSlimFaas.AddHostedService<SlimWorker>();
serviceCollectionSlimFaas.AddHostedService<ScaleReplicasWorker>();
serviceCollectionSlimFaas.AddHostedService<ReplicasSynchronizationWorker>();
serviceCollectionSlimFaas.AddHostedService<HistorySynchronizationWorker>();
serviceCollectionSlimFaas.AddHttpClient();
serviceCollectionSlimFaas.AddSingleton<ISlimFaasQueue, SlimFaasSlimFaasQueue>();
serviceCollectionSlimFaas.AddSingleton<ISlimDataStatus, SlimDataStatus>();
serviceCollectionSlimFaas.AddSingleton<IReplicasService, ReplicasService>(sp =>
    (ReplicasService)serviceProviderStarter.GetService<IReplicasService>()!);
serviceCollectionSlimFaas.AddSingleton<HistoryHttpDatabaseService>();
serviceCollectionSlimFaas.AddSingleton<HistoryHttpMemoryService, HistoryHttpMemoryService>(sp =>
    serviceProviderStarter.GetService<HistoryHttpMemoryService>()!);
serviceCollectionSlimFaas.AddSingleton<IKubernetesService>(sp =>
    serviceProviderStarter.GetService<IKubernetesService>()!);


string publicEndPoint = string.Empty;
string podDataDirectoryPersistantStorage = string.Empty;

string namespace_ = Environment.GetEnvironmentVariable(EnvironmentVariables.Namespace) ??
                    EnvironmentVariables.NamespaceDefault;
Console.WriteLine($"Starting in namespace {namespace_}");
replicasService?.SyncDeploymentsAsync(namespace_).Wait();

string hostname = Environment.GetEnvironmentVariable("HOSTNAME") ?? EnvironmentVariables.HostnameDefault;
while (replicasService?.Deployments.SlimFaas.Pods.Any(p => p.Name == hostname) == false)
{
    Console.WriteLine("Waiting current pod to be ready");
    Thread.Sleep(1000);
    replicasService?.SyncDeploymentsAsync(namespace_).Wait();
}

while (!slimDataAllowColdStart &&
       replicasService?.Deployments.SlimFaas.Pods.Count(p => !string.IsNullOrEmpty(p.Ip)) < 2)
{
    Console.WriteLine("Waiting for at least 2 pods to be ready");
    Thread.Sleep(1000);
    replicasService?.SyncDeploymentsAsync(namespace_).Wait();
}

if (replicasService?.Deployments.SlimFaas.Pods != null)
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
        string slimDataEndpoint = SlimDataEndpoint.Get(podInformation);
        Console.WriteLine($"Adding node  {slimDataEndpoint}");
        Startup.AddClusterMemberBeforeStart(slimDataEndpoint);
    }

    PodInformation currentPod = replicasService.Deployments.SlimFaas.Pods.First(p => p.Name == hostname);
    Console.WriteLine($"Starting node {currentPod.Name}");
    podDataDirectoryPersistantStorage = Path.Combine(slimDataDirectory, currentPod.Name);
    if (Directory.Exists(podDataDirectoryPersistantStorage) == false)
    {
        Directory.CreateDirectory(podDataDirectoryPersistantStorage);
    }

    publicEndPoint = SlimDataEndpoint.Get(currentPod);
    Console.WriteLine($"Node started {currentPod.Name} {publicEndPoint}");
}

serviceCollectionSlimFaas.AddHostedService<SlimDataSynchronizationWorker>();
serviceCollectionSlimFaas.AddSingleton<IDatabaseService, SlimDataService>();
serviceCollectionSlimFaas.AddHttpClient<IDatabaseService, SlimDataService>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
    .AddPolicyHandler(GetRetrySlimDataPolicy())
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = true });

serviceCollectionSlimFaas.AddSingleton<IMasterService, MasterSlimDataService>();

serviceCollectionSlimFaas.AddScoped<ISendClient, SendClient>();
serviceCollectionSlimFaas.AddHttpClient<ISendClient, SendClient>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
    .AddPolicyHandler(GetRetryPolicy());
serviceCollectionSlimFaas.AddOpenTelemetry()
    .WithTracing(builder => builder
        .AddHttpClientInstrumentation()
        .AddAspNetCoreInstrumentation());

if (!string.IsNullOrEmpty(podDataDirectoryPersistantStorage))
{
    builder.Configuration[SlimPersistentState.LogLocation] = podDataDirectoryPersistantStorage;
}


Startup startup = new(builder.Configuration);
int[] slimFaasPorts =
    EnvironmentVariables.ReadIntegers(EnvironmentVariables.SlimFaasPorts, EnvironmentVariables.SlimFaasPortsDefault);

// Node start as master if it is alone in the cluster
string coldStart = replicasService != null && replicasService.Deployments.SlimFaas.Pods.Count == 1 ? "true" : "false";
string slimDataHeartbeatThreshold = Environment.GetEnvironmentVariable(EnvironmentVariables.SlimDataHeartbeatThreshold) ??
                           EnvironmentVariables.SlimDataHeartbeatThresholdDefault;
string slimDataLowerElectionTimeout = Environment.GetEnvironmentVariable(EnvironmentVariables.SlimDataLowerElectionTimeout) ??
                                        EnvironmentVariables.SlimDataLowerElectionTimeoutDefault;
string slimDataUpperElectionTimeout =
    Environment.GetEnvironmentVariable(EnvironmentVariables.SlimDataUpperElectionTimeout) ??
    EnvironmentVariables.SlimDataUpperElectionTimeoutDefault;

Dictionary<string, string> slimDataConfiguration = new()
{
    { "partitioning", "false" },
    { "lowerElectionTimeout", slimDataLowerElectionTimeout },
    { "upperElectionTimeout", slimDataUpperElectionTimeout },
    { "publicEndPoint", publicEndPoint },
    { "coldStart", coldStart },
    { "requestJournal:memoryLimit", "5" },
    { "requestJournal:expiration", "00:01:00" },
    { "heartbeatThreshold", slimDataHeartbeatThreshold }
};
builder.Configuration["publicEndPoint"] = slimDataConfiguration["publicEndPoint"];
startup.ConfigureServices(serviceCollectionSlimFaas);

builder.Host
    .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(slimDataConfiguration!))
    .JoinCluster();

Uri uri = new(publicEndPoint);

builder.WebHost.ConfigureKestrel((context, serverOptions) =>
{
    serverOptions.ListenAnyIP(uri.Port);
    foreach (int slimFaasPort in slimFaasPorts)
    {
        serverOptions.ListenAnyIP(slimFaasPort);
    }
});


WebApplication app = builder.Build();
app.UseMiddleware<SlimProxyMiddleware>();
app.Use(async (context, next) =>
{
    if (!HostPort.IsSamePort(context.Request.Host.Port, slimFaasPorts))
    {
        await next.Invoke();
        return;
    }

    if (context.Request.Path == "/health")
    {
        await context.Response.WriteAsync("OK");
    }
});

startup.Configure(app);

app.UseMetricServer();
app.UseHttpMetrics();

app.Run(async context =>
{
    context.Response.StatusCode = 404;
    await context.Response.WriteAsync("404");
});


app.Run();

serviceProviderStarter.Dispose();

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg =>
        {
            HttpStatusCode[] httpStatusCodesWorthRetrying =
            {
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

static IAsyncPolicy<HttpResponseMessage> GetRetrySlimDataPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg =>
        {
            HttpStatusCode[] httpStatusCodesWorthRetrying =
            {
                HttpStatusCode.RequestTimeout, // 408
                HttpStatusCode.InternalServerError, // 500
                HttpStatusCode.BadGateway, // 502
                HttpStatusCode.ServiceUnavailable, // 503
                HttpStatusCode.GatewayTimeout // 504
            };
            return httpStatusCodesWorthRetrying.Contains(msg.StatusCode);
        })
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromMilliseconds(Math.Pow(2,
            retryAttempt)) * 20);
}


public partial class Program
{
}


#pragma warning restore CA2252
