using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SlimFaas.Tests;

class IMemoryQueue: IQueue
{
    public Task EnqueueAsync(string key, string message)
    {
        throw new NotImplementedException();
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
    public async Task MiddlewareTest_ReturnsNotFoundForRequest()
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
                        services.AddSingleton<IQueue, IMemoryQueue>();
                    })
                    .Configure(app =>
                    {
                        app.UseMiddleware<SlimProxyMiddleware>();
                    });
            })
            .StartAsync();

        var response = await host.GetTestClient().GetAsync("/function/fibonacci/download");
        
    }
}