using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace SlimFaas.Tests;

class MemoryQueue: IQueue
{
    public async Task EnqueueAsync(string key, string message)
    {
        await Task.Delay(1);
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
}

public class ProxyMiddlewareTests
{
    
    [Fact]
    public async Task SlimMiddlewareShouldCallFunctionInSyncModeAndReturnOk()
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
    public async Task SlimMiddlewareShouldCallFunctionInAsyncSyncModeAndReturnOk()
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
                    })
                    .Configure(app =>
                    {
                        app.UseMiddleware<SlimProxyMiddleware>();
                    });
            })
            .StartAsync();

        var response = await host.GetTestClient().GetAsync("/wake-function/fibonacci");
        
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
}