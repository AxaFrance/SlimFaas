﻿using System.Net;
using DotNext.Net.Cluster.Consensus.Raft.Http;

namespace RaftNode;


public class Starter
{

    public static void JoinCluster(WebApplicationBuilder builder)
    {
        builder.JoinCluster();
    }
    
    private static IServiceProvider ServiceProvider { get; set; } = null!;
    
    static Task UseAspNetCoreHost(string publicEndPoint, string? persistentStorage = null)
    {
        var uri = new Uri(publicEndPoint);
        
        var configuration = new Dictionary<string, string>
                {
                    {"partitioning", "false"},
                    {"lowerElectionTimeout", "300" },
                    {"upperElectionTimeout", "600" },
                    {"publicEndPoint", publicEndPoint},
                    {"coldStart", "false"},
                    {"requestJournal:memoryLimit", "5" },
                    {"requestJournal:expiration", "00:01:00" },
                    {"heartbeatThreshold", "0.6" }
                };
        if (!string.IsNullOrEmpty(persistentStorage))
            configuration[SlimPersistentState.LogLocation] = persistentStorage;

        var host = new HostBuilder().ConfigureWebHost(webHost =>
            {
                webHost.UseKestrel(options =>
                    {
                        ServiceProvider = options.ApplicationServices; 
                        options.Listen(IPAddress.Loopback, uri.Port);
                        //options.Listen(IPAddress.Loopback,5000);
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
        builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        //builder.AddDebug();
    }

    public static Task StartNode(string publicEndPoint ="http://localhost:3262", string? persistentStorage = null)
    {
        return UseAspNetCoreHost(publicEndPoint, persistentStorage);
    }
}