﻿using DotNext.Net.Cluster.Consensus.Raft.Http;

namespace RaftNode;


public class Starter
{
    
    public static IServiceProvider ServiceProvider { get; private set; } = null!;
    
    static Task UseAspNetCoreHost(int port, string domain= "localhost", string? persistentStorage = null)
    {
        var configuration = new Dictionary<string, string>
                {
                    {"partitioning", "false"},
                    {"lowerElectionTimeout", "500" },
                    {"upperElectionTimeout", "6000" },
                    {"requestTimeout", "00:10:00"},
                    {"publicEndPoint", $"http://{domain}:{port}"},
                    {"coldStart", "false"},
                    {"requestJournal:memoryLimit", "5" },
                    {"requestJournal:expiration", "00:01:00" }
                };
        if (!string.IsNullOrEmpty(persistentStorage))
            configuration[SimplePersistentState.LogLocation] = persistentStorage;

        var host = new HostBuilder().ConfigureWebHost(webHost =>
            {
                webHost.UseKestrel(options =>
                    {
                        ServiceProvider = options.ApplicationServices;
                        options.ListenLocalhost(port);
                    })
                    .UseStartup<Startup>();
            })
            .ConfigureLogging(ConfigureLogging)
            .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(configuration))
            .JoinCluster()
            .Build();
        
        return host.RunAsync();
    }

    static void ConfigureLogging(ILoggingBuilder builder)
    {
        builder.AddConsole().SetMinimumLevel(LogLevel.Information);
        builder.AddDebug();
    }

    public static Task StartNode(string protocol = "http", int port = 3262, string domain= "localhost", string? persistentStorage = null)
    {
        switch (protocol.ToLowerInvariant())
        {
            case "http":
            case "https":
                return UseAspNetCoreHost(port, domain, persistentStorage);
            default:
                Console.Error.WriteLine("Unsupported protocol type");
                Environment.ExitCode = 1;
                return Task.CompletedTask;
        }
    }
}