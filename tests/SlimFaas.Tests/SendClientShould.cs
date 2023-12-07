using System.Net;
using Microsoft.AspNetCore.Http;

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
        HttpRequestMessage? sendedRequest = null;

        HttpClient httpClient = new HttpClient(new HttpMessageHandlerStub(async (request, cancellationToken) =>
        {
            HttpResponseMessage responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("This is a reply")
            };
            sendedRequest = request;
            return await Task.FromResult(responseMessage);
        }));

        SendClient sendClient = new SendClient(httpClient);
        CustomRequest customRequest =
            new CustomRequest(new List<CustomHeader> { new() { Key = "key", Values = new[] { "value1" } } },
                new byte[1], "fibonacci", "health", httpMethod, "");
        HttpResponseMessage response = await sendClient.SendHttpRequestAsync(customRequest);

        Uri expectedUri = new Uri("http://fibonacci:8080/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(sendedRequest);
        Assert.Equal(sendedRequest.RequestUri, expectedUri);
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
        HttpRequestMessage? sendedRequest = null;
        HttpClient httpClient = new HttpClient(new HttpMessageHandlerStub(async (request, cancellationToken) =>
        {
            HttpResponseMessage responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("This is a reply")
            };
            sendedRequest = request;
            return await Task.FromResult(responseMessage);
        }));

        SendClient sendClient = new SendClient(httpClient);

        DefaultHttpContext httpContext = new DefaultHttpContext();
        HttpRequest httpContextRequest = httpContext.Request;
        string authorization = "bearer value1";
        httpContextRequest.Headers.Add("Authorization", authorization);
        httpContextRequest.Method = httpMethod;
        httpContextRequest.Path = "/fibonacci/health";
        httpContextRequest.Host = new HostString("fibonacci");
        httpContextRequest.Scheme = "http";
        httpContextRequest.Body = new MemoryStream();
        httpContextRequest.Body.WriteByte(1);
        httpContextRequest.Body.Position = 0;
        httpContextRequest.ContentLength = 1;
        httpContextRequest.ContentType = "application/json";

        HttpResponseMessage response = await sendClient.SendHttpRequestSync(httpContext, "fibonacci", "health", "");

        Uri expectedUri = new Uri("http://fibonacci:8080/health");
        Assert.NotNull(sendedRequest);
        Assert.Equal(sendedRequest.RequestUri, expectedUri);
        Assert.Equal(authorization, sendedRequest?.Headers?.Authorization?.ToString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public class HttpMessageHandlerStub : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

    public HttpMessageHandlerStub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) =>
        _sendAsync = sendAsync;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken) => await _sendAsync(request, cancellationToken);
}
