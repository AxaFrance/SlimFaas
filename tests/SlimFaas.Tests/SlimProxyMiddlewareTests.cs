using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using SlimFaas.Kubernetes;

namespace SlimFaas.Tests;

internal class MemoryReplicasService : IReplicasService
{
    public DeploymentsInformations Deployments =>
        new(
            new List<DeploymentInformation>
            {
                new(Replicas: 0, Deployment: "fibonacci", Namespace: "default",
                    Pods: new List<PodInformation> { new("", true, true, "", "") })
            }, new SlimFaasDeploymentInformation(1, new List<PodInformation>()));

    public Task SyncDeploymentsAsync(string kubeNamespace) => throw new NotImplementedException();

    public Task CheckScaleAsync(string kubeNamespace) => throw new NotImplementedException();

    public async Task SyncDeploymentsFromSlimData()
    {
        await Task.Delay(100);
    }
}

internal class MemorySlimFaasQueue : ISlimFaasQueue
{
    public async Task EnqueueAsync(string key, string message) => await Task.Delay(100);

    public Task<IList<string>> DequeueAsync(string key, long count = 1) => throw new NotImplementedException();

    public Task<long> CountAsync(string key) => throw new NotImplementedException();
}

internal class SendClientMock : ISendClient
{
    public Task<HttpResponseMessage> SendHttpRequestAsync(CustomRequest customRequest, HttpContext? context = null)
    {
        HttpResponseMessage responseMessage = new HttpResponseMessage();
        responseMessage.StatusCode = HttpStatusCode.OK;
        return Task.FromResult(responseMessage);
    }

    public Task<HttpResponseMessage> SendHttpRequestSync(HttpContext httpContext, string functionName,
        string functionPath, string functionQuery)
    {
        HttpResponseMessage responseMessage = new HttpResponseMessage();
        responseMessage.StatusCode = HttpStatusCode.OK;
        Task.Delay(100).Wait();
        return Task.FromResult(responseMessage);
    }
}

public class ProxyMiddlewareTests
{
    [Theory]
    [InlineData("/function/fibonacci/download", HttpStatusCode.OK)]
    [InlineData("/function/wrong/download", HttpStatusCode.NotFound)]
    public async Task CallFunctionInSyncModeAndReturnOk(string path, HttpStatusCode expected)
    {
        Mock<IWakeUpFunction> wakeUpFunctionMock = new();
        HttpResponseMessage responseMessage = new HttpResponseMessage();
        responseMessage.StatusCode = HttpStatusCode.OK;
        Mock<ISendClient> sendClientMock = new Mock<ISendClient>();
        sendClientMock.Setup(s => s.SendHttpRequestAsync(It.IsAny<CustomRequest>(), It.IsAny<HttpContext>()))
            .ReturnsAsync(responseMessage);

        using IHost host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton<HistoryHttpMemoryService, HistoryHttpMemoryService>();
                        services.AddSingleton<ISendClient, SendClientMock>();
                        services.AddSingleton<ISlimFaasQueue, MemorySlimFaasQueue>();
                        services.AddSingleton<IReplicasService, MemoryReplicasService>();
                        services.AddSingleton<IWakeUpFunction>(sp => wakeUpFunctionMock.Object);
                    })
                    .Configure(app => { app.UseMiddleware<SlimProxyMiddleware>(); });
            })
            .StartAsync();

        HttpResponseMessage response = await host.GetTestClient().GetAsync($"http://localhost:5000{path}");

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("/async-function/fibonacci/download", HttpStatusCode.Accepted)]
    [InlineData("/async-function/wrong/download", HttpStatusCode.NotFound)]
    public async Task CallFunctionInAsyncSyncModeAndReturnOk(string path, HttpStatusCode expected)
    {
        Mock<IWakeUpFunction> wakeUpFunctionMock = new();
        using IHost host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton<HistoryHttpMemoryService, HistoryHttpMemoryService>();
                        services.AddSingleton<ISendClient, SendClientMock>();
                        services.AddSingleton<ISlimFaasQueue, MemorySlimFaasQueue>();
                        services.AddSingleton<IReplicasService, MemoryReplicasService>();
                        services.AddSingleton<IWakeUpFunction>(sp => wakeUpFunctionMock.Object);
                    })
                    .Configure(app => { app.UseMiddleware<SlimProxyMiddleware>(); });
            })
            .StartAsync();

        HttpResponseMessage response = await host.GetTestClient().GetAsync($"http://localhost:5000{path}");

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("/wake-function/fibonacci", HttpStatusCode.NoContent, 1)]
    [InlineData("/wake-function/wrong", HttpStatusCode.NotFound, 0)]
    public async Task JustWakeFunctionAndReturnOk(string path, HttpStatusCode expectedHttpStatusCode,
        int numberFireAndForgetWakeUpAsyncCall)
    {
        Mock<IWakeUpFunction> wakeUpFunctionMock = new();
        wakeUpFunctionMock.Setup(k => k.FireAndForgetWakeUpAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        using IHost host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton<HistoryHttpMemoryService, HistoryHttpMemoryService>();
                        services.AddSingleton<ISendClient, SendClientMock>();
                        services.AddSingleton<ISlimFaasQueue, MemorySlimFaasQueue>();
                        services.AddSingleton<IReplicasService, MemoryReplicasService>();
                        services.AddSingleton<IWakeUpFunction>(sp => wakeUpFunctionMock.Object);
                    })
                    .Configure(app => { app.UseMiddleware<SlimProxyMiddleware>(); });
            })
            .StartAsync();

        HttpResponseMessage response = await host.GetTestClient().GetAsync($"http://localhost:5000{path}");
        HistoryHttpMemoryService historyHttpMemoryService =
            host.Services.GetRequiredService<HistoryHttpMemoryService>();

        wakeUpFunctionMock.Verify(k => k.FireAndForgetWakeUpAsync(It.IsAny<string>()), Times.AtMost(numberFireAndForgetWakeUpAsyncCall));
        Assert.Equal(expectedHttpStatusCode, response.StatusCode);
    }

    [Theory]
    [InlineData("/status-function/fibonacci", HttpStatusCode.OK, "{\"NumberReady\":1,\"NumberRequested\":0}")]
    [InlineData("/status-function/wrong", HttpStatusCode.NotFound, "")]
    public async Task GetStatusFunctionAndReturnOk(string path, HttpStatusCode expectedHttpStatusCode,
        string expectedBody)
    {
        Mock<IWakeUpFunction> wakeUpFunctionMock = new();
        using IHost host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton<HistoryHttpMemoryService, HistoryHttpMemoryService>();
                        services.AddSingleton<ISendClient, SendClientMock>();
                        services.AddSingleton<ISlimFaasQueue, MemorySlimFaasQueue>();
                        services.AddSingleton<IReplicasService, MemoryReplicasService>();
                        services.AddSingleton<IWakeUpFunction>(sp => wakeUpFunctionMock.Object);
                    })
                    .Configure(app => { app.UseMiddleware<SlimProxyMiddleware>(); });
            })
            .StartAsync();

        HttpResponseMessage response = await host.GetTestClient().GetAsync($"http://localhost:5000{path}");
        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal(expectedBody, body);
        Assert.Equal(expectedHttpStatusCode, response.StatusCode);
    }
}
