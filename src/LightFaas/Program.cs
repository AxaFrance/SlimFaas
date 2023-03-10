using System.Net;
using LightFaas;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHostedService<FaasWorker>();
builder.Services.AddHttpClient();/*Options.DefaultName).ConfigurePrimaryHttpMessageHandler(() =>
{
    return new HttpClientHandler
    {
        ClientCertificateOptions = ClientCertificateOption.Manual,
        ServerCertificateCustomValidationCallback =
            (httpRequestMessage, cert, certChain, policyErrors) => true
    };
});;*/
builder.Services.AddSingleton<IQueue, Queue>();
builder.Services.AddSingleton<KubernetesService, KubernetesService>();
builder.Services.AddScoped<SendClient, SendClient>();
builder.Services.AddHttpClient<SendClient, SendClient>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
    .AddPolicyHandler(GetRetryPolicy());
var app = builder.Build();
app.UseMetricServer();
app.UseHttpMetrics();
app.UseMiddleware<FaasMiddleware>();

app.Run(async context =>
{
    if (context.Request.Path == "/health")
    {
        await context.Response.WriteAsync("OK");
    }
    else
    {
        context.Response.StatusCode = 404;
    }
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


public record CustomRequest
{
    public IList<CustomHeader> Headers { get; init; }
    public IList<CustomForm> Form { get; init; }
    public IList<CustomFormFile> FormFiles { get; init; }
    public string FunctionName { get; init; }
    public string Path { get; init; }
    public string Method { get; init; }
    public string Query { get; set; }   
    public string Body { get; set; }
    public string ContentType { get; set; }
}

public record CustomHeader
{
    public string Key { get; init; }
    public string?[] Values { get; init; } 
}

public record CustomForm
{
    public string Key { get; init; }
    public string?[] Values { get; init; } 
}

public record CustomFormFile
{
    public string Key { get; init; }
    public byte[] Value { get; init; }
    public string Filename { get; set; }
} 