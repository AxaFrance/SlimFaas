using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace SlimFaas.Tests;

public class ProgramShould
{
    [Fact]
    public async Task TestRootEndpoint()
    {
        Environment.SetEnvironmentVariable("MOCK_REDIS", "true");
        Environment.SetEnvironmentVariable("MOCK_KUBERNETES_FUNCTIONS", "{\"Functions\":[{\"Name\":\"fibonacci1\",\"NumberParallelRequest\":1},{\"Name\":\"fibonacci2\",\"NumberParallelRequest\":1}]}");
        await using var application = new WebApplicationFactory<Program>();
        using var client = application.CreateClient();

        var response = await client.GetStringAsync("/health");
  
        Assert.Equal("OK", response);
    }
}