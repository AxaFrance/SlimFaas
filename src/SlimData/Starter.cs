using DotNext.Net.Cluster.Consensus.Raft.Http;

namespace RaftNode;


public class Starter
{
    
    public static IServiceProvider ServiceProvider { get; private set; } = null!;
    
    static Task UseAspNetCoreHost(string publicEndPoint, string? persistentStorage = null)
    {
        var uri = new Uri(publicEndPoint);
        
        var configuration = new Dictionary<string, string>
                {
                    {"partitioning", "false"},
                    {"lowerElectionTimeout", "150" },
                    {"upperElectionTimeout", "300" },
                    {"requestTimeout", "00:01:00"},
                    {"publicEndPoint", publicEndPoint},
                    {"coldStart", "false"},
                    {"requestJournal:memoryLimit", "5" },
                    {"requestJournal:expiration", "00:01:00" },
                    {"heartbeatThreshold", "0.6" }
                };
        if (!string.IsNullOrEmpty(persistentStorage))
            configuration[SimplePersistentState.LogLocation] = persistentStorage;

        var host = new HostBuilder().ConfigureWebHost(webHost =>
            {
                webHost.UseKestrel(options =>
                    {
                        ServiceProvider = options.ApplicationServices; 
                        options.ListenAnyIP(uri.Port);
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