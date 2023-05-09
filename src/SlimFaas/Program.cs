using System.Net;
using OpenTelemetry.Trace;
using SlimFaas;
using Polly;
using Polly.Extensions.Http;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

var serviceCollection = builder.Services;
serviceCollection.AddHostedService<SlimWorker>();
serviceCollection.AddHostedService<ScaleReplicasWorker>();
serviceCollection.AddHostedService<MasterWorker>();
serviceCollection.AddHostedService<ReplicasSynchronizationWorker>();
serviceCollection.AddHostedService<HistorySynchronizationWorker>();
serviceCollection.AddHttpClient();
serviceCollection.AddSingleton<IQueue, RedisQueue>();
serviceCollection.AddSingleton<ReplicasService, ReplicasService>();
serviceCollection.AddSingleton<RedisService, RedisService>();
serviceCollection.AddSingleton<MasterService, MasterService>();
serviceCollection.AddSingleton<HistoryHttpRedisService, HistoryHttpRedisService>();
serviceCollection.AddSingleton<HistoryHttpMemoryService, HistoryHttpMemoryService>();

var mockKubernetesFunction = Environment.GetEnvironmentVariable("MOCK_KUBERNETES_FUNCTIONS");
if (!string.IsNullOrEmpty(mockKubernetesFunction))
{
    serviceCollection.AddSingleton<IKubernetesService, MockKubernetesService>();
}
else
{
    serviceCollection.AddSingleton<IKubernetesService, KubernetesService>();
}
serviceCollection.AddScoped<SendClient, SendClient>();
serviceCollection.AddHttpClient<SendClient, SendClient>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
    .AddPolicyHandler(GetRetryPolicy());
serviceCollection.AddOpenTelemetry()
    .WithTracing(builder => builder
        .AddHttpClientInstrumentation()
        .AddAspNetCoreInstrumentation());
        //.AddConsoleExporter());

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
app.UseMiddleware<SlimMiddleware>();

app.Run(context =>
{
    context.Response.StatusCode = 404;
    return Task.CompletedTask;
});

app.Run();

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
