using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace RaftNode;

internal sealed class RaftClientHandlerFactory : IHttpMessageHandlerFactory
{
    public HttpMessageHandler CreateHandler(string name)
    {
        var slimDataUpperElectionTimeout = Environment.GetEnvironmentVariable("SLIMDATA_UPPER_ELECTION_TIMEOUT") ?? "600";
        if (!int.TryParse(slimDataUpperElectionTimeout, out int upperElectionTimeout))
        {
            throw new Exception("SLIMDATA_UPPER_ELECTION_TIMEOUT is not an integer");
        }
        var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromMilliseconds(upperElectionTimeout) };
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