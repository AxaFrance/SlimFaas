using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace RaftNode;

internal sealed class RaftClientHandlerFactory : IHttpMessageHandlerFactory
{
    private readonly int _connectTimeoutMs;

    public RaftClientHandlerFactory(int connectTimeoutMs)
    {
        _connectTimeoutMs = connectTimeoutMs;
    }
    public HttpMessageHandler CreateHandler(string name)
    {
        var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromMilliseconds(_connectTimeoutMs) };
        handler.SslOptions.RemoteCertificateValidationCallback = AllowCertificate;
        handler.UseProxy = false;
        return handler;
    }

    internal static bool AllowCertificate(object sender, X509Certificate? certificate, X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        return true;
    }
}