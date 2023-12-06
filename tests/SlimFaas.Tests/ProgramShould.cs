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
        Environment.SetEnvironmentVariable(EnvironmentVariables.BaseSlimDataUrl, "http://localhost:3262/");
        Environment.SetEnvironmentVariable(EnvironmentVariables.MockKubernetesFunctions, "{\"Functions\":[{\"Name\":\"fibonacci1\",\"NumberParallelRequest\":1},{\"Name\":\"fibonacci2\",\"NumberParallelRequest\":1}],\"Slimfaas\":[{\"Name\":\"slimfaas-1\"}]}");
#pragma warning disable CA2252
        await using var application = new WebApplicationFactory<Program>();
#pragma warning restore CA2252
        using var client = application.CreateClient();

        var response = await client.GetStringAsync("http://localhost:5000/health");

        Assert.Equal("OK", response);
    }
}
