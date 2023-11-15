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


class MemoryReplicasService : IReplicasService
{
    public DeploymentsInformations Deployments =>
        new(
            Functions: new List<DeploymentInformation>()
            {
                new(Replicas: 0, Deployment: "fibonacci", Namespace: "default",
                    Pods: new List<PodInformation> { new("", true, true, "", "") })
            }, SlimFaas: new SlimFaasDeploymentInformation(Replicas: 1, new List<PodInformation>()));

    public Task SyncDeploymentsAsync(string kubeNamespace)
    {
        throw new NotImplementedException();
    }

    public Task CheckScaleAsync(string kubeNamespace)
    {
        throw new NotImplementedException();
    }
}

class MemorySlimFaasQueue: ISlimFaasQueue
{
    public async Task EnqueueAsync(string key, string message)
    {
        await Task.Delay(100);
    }

    public Task<IList<string>> DequeueAsync(string key, long count = 1)
    {
        throw new NotImplementedException();
    }

    public Task<long> CountAsync(string key)
    {
        throw new NotImplementedException();
    }
}

class SendClientMock : ISendClient
{
    public Task<HttpResponseMessage> SendHttpRequestAsync(CustomRequest customRequest, HttpContext? context = null)
    {
        var responseMessage = new HttpResponseMessage();
        responseMessage.StatusCode = HttpStatusCode.OK;
        return Task.FromResult(responseMessage);
    }

    public Task<HttpResponseMessage> SendHttpRequestSync(HttpContext httpContext, string functionName, string functionPath, string functionQuery)
    {
        var responseMessage = new HttpResponseMessage();
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
        var responseMessage = new HttpResponseMessage();
        responseMessage.StatusCode = HttpStatusCode.OK;
        var sendClientMock = new Mock<ISendClient>();
        sendClientMock.Setup(s => s.SendHttpRequestAsync(It.IsAny<CustomRequest>(), It.IsAny<HttpContext>()))
            .ReturnsAsync(responseMessage);

        using var host = await new HostBuilder()
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
                    })
                    .Configure(app =>
                    {
                        app.UseMiddleware<SlimProxyMiddleware>();
                    });
            })
            .StartAsync();

        var response = await host.GetTestClient().GetAsync(path);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("/async-function/fibonacci/download", HttpStatusCode.Accepted)]
    [InlineData("/async-function/wrong/download", HttpStatusCode.NotFound)]
    public async Task CallFunctionInAsyncSyncModeAndReturnOk(string path, HttpStatusCode expected)
    {
        using var host = await new HostBuilder()
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
                    })
                    .Configure(app =>
                    {
                        app.UseMiddleware<SlimProxyMiddleware>();
                    });
            })
            .StartAsync();

        var response = await host.GetTestClient().GetAsync(path);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("/wake-function/fibonacci", HttpStatusCode.NoContent, true)]
    [InlineData("/wake-function/wrong", HttpStatusCode.NotFound, false)]
    public async Task JustWakeFunctionAndReturnOk(string path, HttpStatusCode expectedHttpStatusCode, bool expectedTickFound)
    {
        using var host = await new HostBuilder()
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
                    })
                    .Configure(app =>
                    {
                        app.UseMiddleware<SlimProxyMiddleware>();
                    });
            })
            .StartAsync();

        var response = await host.GetTestClient().GetAsync(path);
        var historyHttpMemoryService = host.Services.GetRequiredService<HistoryHttpMemoryService>();
        var ticksLastCall = historyHttpMemoryService.GetTicksLastCall("fibonacci");

        Assert.Equal(ticksLastCall > 0, expectedTickFound);
        Assert.Equal(expectedHttpStatusCode, response.StatusCode);
    }

}
