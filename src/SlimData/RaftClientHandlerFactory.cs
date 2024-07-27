﻿using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace SlimData;

internal sealed class RaftClientHandlerFactory : IHttpMessageHandlerFactory
{
    public HttpMessageHandler CreateHandler(string name)
    {
        var slimDataSocketsHttpHandlerTimeoutDefault =
            Environment.GetEnvironmentVariable(EnvironmentVariables.SlimDataSocketsHttpHandlerTimeout) ??
            EnvironmentVariables.SlimDataSocketsHttpHandlerTimeoutDefault;
        if (!int.TryParse(slimDataSocketsHttpHandlerTimeoutDefault, out int electionTimeout))
        {
            throw new Exception("SLIMDATA_SOCKETS_HTTP_HANDLER_TIMEOUT is not an integer");
        }
        Console.WriteLine($"RaftClientHandlerFactory.CreateHandler({name}) with electionTimeout {electionTimeout}");
        var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromMilliseconds(electionTimeout) };
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