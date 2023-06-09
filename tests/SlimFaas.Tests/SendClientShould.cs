using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using RichardSzalay.MockHttp;

namespace SlimFaas.Tests;

public class SendClientShould
{
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("HEAD")]
    public async Task CallFunction(string httpMethid)
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When("http://fibonacci:8080/health")
            .Respond("application/json", "{'ok' : true}");

        var sendClient = new SendClient(mockHttp.ToHttpClient());
        var customRequest = new CustomRequest(new List<CustomHeader> { new() { Key = "key", Values = new []{"value1"}}}, new byte[1], "fibonacci", "health", httpMethid, "");
        var response = await sendClient.SendHttpRequestAsync(customRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}