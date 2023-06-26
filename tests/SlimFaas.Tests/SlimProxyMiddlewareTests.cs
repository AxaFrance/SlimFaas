using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace SlimFaas.Tests;


class MemoryReplicasService : IReplicasService
{
    public DeploymentsInformations Deployments =>
        new()
        {
            Functions = new List<DeploymentInformation>()
            {
                new()
                {
                    Replicas = 0, 
                    Deployment = "fibonacci", 
                    Namespace = "default",
                    Pods = new List<PodInformation> { new() { Ready = true } }
                }
            },
            SlimFaas = new SlimFaasDeploymentInformation
            {
                Replicas = 1
            }
        };

    public Task SyncDeploymentsAsync(string kubeNamespace)
    {
        throw new NotImplementedException();
    }

    public Task CheckScaleAsync(string kubeNamespace)
    {
        throw new NotImplementedException();
    }
}

class MemoryQueue: IQueue
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
        return Task.FromResult(responseMessage);
    }
}

public class ProxyMiddlewareTests
{
    
    [Fact]
    public async Task CallFunctionInSyncModeAndReturnOk()
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
                        services.AddSingleton<IQueue, MemoryQueue>();
                        services.AddSingleton<IReplicasService, MemoryReplicasService>();
                    })
                    .Configure(app =>
                    {
                        app.UseMiddleware<SlimProxyMiddleware>();
                    });
            })
            .StartAsync();
        
        var response = await host.GetTestClient().GetAsync("/function/fibonacci/download");
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    
    [Fact]
    public async Task CallFunctionInAsyncSyncModeAndReturnOk()
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
                        services.AddSingleton<IQueue, MemoryQueue>();
                        services.AddSingleton<IReplicasService, MemoryReplicasService>();
                    })
                    .Configure(app =>
                    {
                        app.UseMiddleware<SlimProxyMiddleware>();
                    });
            })
            .StartAsync();

        var response = await host.GetTestClient().GetAsync("/async-function/fibonacci/download");
        
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
    
    [Fact]
    public async Task JustWakeFunctionAndReturnOk()
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
                        services.AddSingleton<IQueue, MemoryQueue>();
                        services.AddSingleton<IReplicasService, MemoryReplicasService>();
                    })
                    .Configure(app =>
                    {
                        app.UseMiddleware<SlimProxyMiddleware>();
                    });
            })
            .StartAsync();
        
        var response = await host.GetTestClient().GetAsync("/wake-function/fibonacci");
        var historyHttpMemoryService = host.Services.GetService<HistoryHttpMemoryService>();
        var ticksLastCall = historyHttpMemoryService.GetTicksLastCall("fibonacci");
        
        Assert.True(ticksLastCall > 0);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
    }
}