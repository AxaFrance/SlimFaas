using DotNext.Net.Cluster.Consensus.Raft.Http;

namespace RaftNode;

public class Starter
{
    
    static Task UseAspNetCoreHost(int port, string? persistentStorage = null)
    {
        var configuration = new Dictionary<string, string>
                {
                    {"partitioning", "false"},
                    {"lowerElectionTimeout", "500" },
                    {"upperElectionTimeout", "6000" },
                    {"requestTimeout", "00:10:00"},
                    {"publicEndPoint", $"http://localhost:{port}"},
                    {"coldStart", "false"},
                    {"requestJournal:memoryLimit", "5" },
                    {"requestJournal:expiration", "00:01:00" }
                };
        if (!string.IsNullOrEmpty(persistentStorage))
            configuration[SimplePersistentState.LogLocation] = persistentStorage;
        return new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseKestrel(options =>
            {
                options.ListenLocalhost(port);
            })
            .UseStartup<Startup>();
        })
        .ConfigureLogging(ConfigureLogging)
        .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(configuration))
        .JoinCluster()
        .Build()
        .RunAsync();
    }

    static void ConfigureLogging(ILoggingBuilder builder)
        => builder.AddConsole().SetMinimumLevel(LogLevel.Error);
    
    public static Task StartNode(string protocol = "http", int port = 3262, string? persistentStorage = null)
    {
        switch (protocol.ToLowerInvariant())
        {
            case "http":
            case "https":
                return UseAspNetCoreHost(port, persistentStorage);
            default:
                Console.Error.WriteLine("Unsupported protocol type");
                Environment.ExitCode = 1;
                return Task.CompletedTask;
        }
    }
}