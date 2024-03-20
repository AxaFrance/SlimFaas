using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using DotNext;
using DotNext.Buffers;
using DotNext.Diagnostics;
using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.Http;
using MemoryPack;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RaftNode;
using SlimFaas;
using SlimFaas.Database;

namespace SlimData.Tests;

[ExcludeFromCodeCoverage]
internal sealed class AdvancedDebugProvider : Disposable, ILoggerProvider
{
    private readonly string prefix;

    internal AdvancedDebugProvider(string prefix) => this.prefix = prefix;

    public ILogger CreateLogger(string name) => new Logger(name, prefix);

    private sealed class Logger : ILogger
    {
        private readonly string prefix, name;

        internal Logger(string name, string prefix)
        {
            this.prefix = prefix;
            this.name = name;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => Debugger.IsAttached && logLevel is not LogLevel.None;

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter?.Invoke(state, exception);

            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            BufferWriterSlim<char> buffer = new BufferWriterSlim<char>(stackalloc char[128]);
            //buffer.WriteString($"[{prefix}]({new Timestamp()}){logLevel}: {message}");

            if (exception is not null)
            {
                buffer.WriteLine();
                buffer.WriteLine();
                buffer.Write(exception.ToString());
            }

            message = buffer.ToString();
            buffer.Dispose();

            Debug.WriteLine(message, name);
        }
    }
}

[ExcludeFromCodeCoverage]
internal static class TestLoggers
{
    private static AdvancedDebugProvider CreateProvider(this string prefix, IServiceProvider services)
        => new(prefix);

    internal static ILoggingBuilder AddDebugLogger(this ILoggingBuilder builder, string prefix)
    {
        AddDebugLogger(prefix, builder);
        return builder;
    }

    private static void AddDebugLogger(this string prefix, ILoggingBuilder builder)
        => builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, AdvancedDebugProvider>(prefix.CreateProvider));

    internal static ILoggerFactory CreateDebugLoggerFactory(string prefix, Action<ILoggingBuilder> builder)
        => LoggerFactory.Create(prefix.AddDebugLogger + builder);
}

[ExcludeFromCodeCoverage]
internal class LeaderChangedEvent : TaskCompletionSource<IClusterMember>
{
    internal LeaderChangedEvent()
        : base(TaskCreationOptions.RunContinuationsAsynchronously)
    {
    }

    internal void OnLeaderChanged(ICluster sender, IClusterMember leader)
    {
        if (leader is not null)
        {
            TrySetResult(leader);
        }
    }
}

internal sealed class LeaderTracker : LeaderChangedEvent, IClusterMemberLifetime
{
    void IClusterMemberLifetime.OnStart(IRaftCluster cluster, IDictionary<string, string> metadata)
        => cluster.LeaderChanged += OnLeaderChanged;

    void IClusterMemberLifetime.OnStop(IRaftCluster cluster)
        => cluster.LeaderChanged -= OnLeaderChanged;
}

public class RaftClusterTests
{
    private protected static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    private static IHost CreateHost<TStartup>(int port, IDictionary<string, string> configuration,
        IClusterMemberLifetime configurator = null,
        Func<TimeSpan, IRaftClusterMember, IFailureDetector> failureDetectorFactory = null)
        where TStartup : class =>
        new HostBuilder()
            .ConfigureWebHost(webHost => webHost.UseKestrel(options => options.ListenLocalhost(port))
                .ConfigureServices(services =>
                {
                    if (configurator is not null)
                    {
                        services.AddSingleton(configurator);
                    }

                    if (failureDetectorFactory is not null)
                    {
                        services.AddSingleton(failureDetectorFactory);
                    }

                    services.AddSingleton<IDatabaseService, SlimDataService>();
                    services.AddHttpClient<IDatabaseService, SlimDataService>()
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = true });
                })
                .UseStartup<TStartup>()
            )
            .ConfigureHostOptions(static options => options.ShutdownTimeout = DefaultTimeout)
            .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(configuration))
            .ConfigureLogging(builder => builder.AddDebugLogger(port.ToString()).SetMinimumLevel(LogLevel.Debug))
            .JoinCluster()
            .Build();

    private static IRaftHttpCluster GetLocalClusterView(IHost host)
        => host.Services.GetRequiredService<IRaftHttpCluster>();

    public static string GetTemporaryDirectory()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        if (File.Exists(tempDirectory))
        {
            return GetTemporaryDirectory();
        }

        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }

    [Fact(Timeout = 20000)]
    public static async Task MessageExchange()
    {
        Dictionary<string, string> config1 = new()
        {
            { "partitioning", "false" },
            { "lowerElectionTimeout", "600" },
            { "upperElectionTimeout", "900" },
            { "publicEndPoint", "http://localhost:3262/" },
            { "coldStart", "true" },
            { "requestTimeout", "00:01:00" },
            { SlimPersistentState.LogLocation, GetTemporaryDirectory() }
        };

        Dictionary<string, string> config2 = new()
        {
            { "partitioning", "false" },
            { "lowerElectionTimeout", "600" },
            { "upperElectionTimeout", "900" },
            { "publicEndPoint", "http://localhost:3263/" },
            { "coldStart", "false" },
            { "requestTimeout", "00:01:00" },
            { SlimPersistentState.LogLocation, GetTemporaryDirectory() }
        };

        Dictionary<string, string> config3 = new()
        {
            { "partitioning", "false" },
            { "lowerElectionTimeout", "600" },
            { "upperElectionTimeout", "900" },
            { "publicEndPoint", "http://localhost:3264/" },
            { "coldStart", "false" },
            { "requestTimeout", "00:01:00" },
            { SlimPersistentState.LogLocation, GetTemporaryDirectory() }
        };

        LeaderTracker listener = new LeaderTracker();
        using IHost host1 = CreateHost<Startup>(3262, config1, listener);
        await host1.StartAsync();
        Assert.True(GetLocalClusterView(host1).Readiness.IsCompletedSuccessfully);

        using IHost host2 = CreateHost<Startup>(3263, config2);
        await host2.StartAsync();

        using IHost host3 = CreateHost<Startup>(3264, config3);
        await host3.StartAsync();

        while (GetLocalClusterView(host1).Leader == null)
        {
            await Task.Delay(200);
        }

        Assert.True(await GetLocalClusterView(host1).AddMemberAsync(GetLocalClusterView(host2).LocalMemberAddress));
        await GetLocalClusterView(host2).Readiness.WaitAsync(DefaultTimeout);

        Assert.True(await GetLocalClusterView(host1).AddMemberAsync(GetLocalClusterView(host3).LocalMemberAddress));
        await GetLocalClusterView(host3).Readiness.WaitAsync(DefaultTimeout);

        IDatabaseService databaseServiceSlave = host3.Services.GetRequiredService<IDatabaseService>();
        IDatabaseService databaseServiceMaster = host1.Services.GetRequiredService<IDatabaseService>();

        await databaseServiceSlave.SetAsync("key1", MemoryPackSerializer.Serialize("value1") );
        Assert.Equal("value1", MemoryPackSerializer.Deserialize<string>(await databaseServiceMaster.GetAsync("key1")));
        Assert.Equal("value1", MemoryPackSerializer.Deserialize<string>(await databaseServiceSlave.GetAsync("key1")));

        await databaseServiceSlave.HashSetAsync("hashsetKey1",
            new Dictionary<string, string> { { "field1", "value1" }, { "field2", "value2" } });
        IDictionary<string, string> hashGet = await databaseServiceSlave.HashGetAllAsync("hashsetKey1");

        Assert.Equal("value1", hashGet["field1"]);
        Assert.Equal("value2", hashGet["field2"]);

       await databaseServiceSlave.ListLeftPushAsync("listKey1",   MemoryPackSerializer.Serialize("value1"));

        long listLength = await databaseServiceSlave.ListLengthAsync("listKey1");
        Assert.Equal(1, listLength);

        IList<byte[]> listRightPop = await databaseServiceSlave.ListRightPopAsync("listKey1");
        Assert.Equal("value1", MemoryPackSerializer.Deserialize<string>(listRightPop[0]));

        await host1.StopAsync();
        await host2.StopAsync();
        await host3.StopAsync();
    }
}
