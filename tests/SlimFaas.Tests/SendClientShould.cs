using System.Net;
using Microsoft.AspNetCore.Http;
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
    [InlineData("PUT")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public async Task CallFunctionAsync(string httpMethod)
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When("http://fibonacci:8080/health")
            .Respond("application/json", "{'ok' : true}");

        var sendClient = new SendClient(mockHttp.ToHttpClient());
        var customRequest = new CustomRequest(new List<CustomHeader> { new() { Key = "key", Values = new []{"value1"}}}, new byte[1], "fibonacci", "health", httpMethod, "");
        var response = await sendClient.SendHttpRequestAsync(customRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    
    
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("HEAD")]
    [InlineData("PUT")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public async Task CallFunctionSync(string httpMethod)
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When("http://fibonacci:8080/health")
            .Respond("application/json", "{'ok' : true}");

        var sendClient = new SendClient(mockHttp.ToHttpClient());
        
        var httpContext = new DefaultHttpContext();
        var httpContextRequest = httpContext.Request;   
        httpContextRequest.Headers.Add("Authorization", "bearer value1");
        httpContextRequest.Method = httpMethod;
        httpContextRequest.Path = "/fibonacci/health";
        httpContextRequest.Host = new HostString("fibonacci");
        httpContextRequest.Scheme = "http";
        httpContextRequest.Body = new MemoryStream();
        httpContextRequest.Body.WriteByte(1);
        httpContextRequest.Body.Position = 0;
        httpContextRequest.ContentLength = 1;
        httpContextRequest.ContentType = "application/json";
        
        var response = await sendClient.SendHttpRequestSync(httpContext, "fibonacci", "health", "");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}