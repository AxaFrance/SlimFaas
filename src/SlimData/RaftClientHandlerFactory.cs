using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace SlimData;

internal sealed class RaftClientHandlerFactory : IHttpMessageHandlerFactory
{
    public HttpMessageHandler CreateHandler(string name)
    {
        Console.WriteLine($"RaftClientHandlerFactory.CreateHandler({name})");
        {
            var slimDataSocketsHttpHandlerTimeoutDefault =
                Environment.GetEnvironmentVariable(EnvironmentVariables.SlimDataSocketsHttpHandlerTimeout) ??
                EnvironmentVariables.SlimDataSocketsHttpHandlerTimeoutDefault;
            if (!int.TryParse(slimDataSocketsHttpHandlerTimeoutDefault, out int upperElectionTimeout))
            {
                throw new Exception("SLIMDATA_SOCKETS_HTTP_HANDLER_TIMEOUT is not an integer");
            }

            var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromMilliseconds(upperElectionTimeout) };
            handler.SslOptions.RemoteCertificateValidationCallback = AllowCertificate;
            handler.UseProxy = false;
            return handler;
        } 
    }

    internal static bool AllowCertificate(object sender, X509Certificate? certificate, X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        return true;
    }
}