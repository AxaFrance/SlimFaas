using System.Net;
using SlimFaas;
using Polly;
using Polly.Extensions.Http;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHostedService<SlimWorker>();
builder.Services.AddHostedService<ScaleReplicasWorker>();
builder.Services.AddHostedService<MasterWorker>();
builder.Services.AddHostedService<ReplicasSyncWorker>();
builder.Services.AddHostedService<SyncHistoryWorker>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IQueue, RedisQueue>();
builder.Services.AddSingleton<ReplicasService, ReplicasService>();
builder.Services.AddSingleton<RedisService, RedisService>();
builder.Services.AddSingleton<MasterService, MasterService>();
builder.Services.AddSingleton<HistoryHttpRedisService, HistoryHttpRedisService>();
builder.Services.AddSingleton<HistoryHttpMemoryService, HistoryHttpMemoryService>();

var mockKubernetesFunction = Environment.GetEnvironmentVariable("MOCK_KUBERNETES_FUNCTIONS");
if (!string.IsNullOrEmpty(mockKubernetesFunction))
{
    builder.Services.AddSingleton<IKubernetesService, MockKubernetesService>();
}
else
{
    builder.Services.AddSingleton<IKubernetesService, KubernetesService>();
}
builder.Services.AddScoped<SendClient, SendClient>();
builder.Services.AddHttpClient<SendClient, SendClient>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
    .AddPolicyHandler(GetRetryPolicy());
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
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(3,
            retryAttempt)));
}
