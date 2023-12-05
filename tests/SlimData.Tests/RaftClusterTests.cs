using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using DotNext;
using DotNext.Buffers;
using DotNext.Diagnostics;
using DotNext.Net;
using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.Http;
using DotNext.Net.Cluster.Messaging;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RaftNode;
using SlimFaas;

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
            => NullLogger.Instance.BeginScope<TState>(state);

        public bool IsEnabled(LogLevel logLevel) => Debugger.IsAttached && logLevel is not LogLevel.None;

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            string message = formatter?.Invoke(state, exception);

            if (string.IsNullOrEmpty(message))
                return;

            var buffer = new BufferWriterSlim<char>(stackalloc char[128]);
            buffer.WriteString($"[{prefix}]({new Timestamp()}){logLevel}: {message}");

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
        => builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, AdvancedDebugProvider>(prefix.CreateProvider));

    internal static ILoggerFactory CreateDebugLoggerFactory(string prefix, Action<ILoggingBuilder> builder)
        => LoggerFactory.Create(prefix.AddDebugLogger + builder);
}

[ExcludeFromCodeCoverage]
class LeaderChangedEvent : TaskCompletionSource<IClusterMember>
{
    internal LeaderChangedEvent()
        : base(TaskCreationOptions.RunContinuationsAsynchronously)
    {
    }

    internal void OnLeaderChanged(ICluster sender, IClusterMember leader)
    {
        if (leader is not null)
            TrySetResult(leader);
    }
}

sealed class LeaderTracker : LeaderChangedEvent, IClusterMemberLifetime
{
    void IClusterMemberLifetime.OnStart(IRaftCluster cluster, IDictionary<string, string> metadata)
        => cluster.LeaderChanged += OnLeaderChanged;

    void IClusterMemberLifetime.OnStop(IRaftCluster cluster)
        => cluster.LeaderChanged -= OnLeaderChanged;
}

public class RaftClusterTests
{
    private protected static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    private static IHost CreateHost<TStartup>(int port, IDictionary<string, string> configuration, IClusterMemberLifetime configurator = null, Func<TimeSpan, IRaftClusterMember, IFailureDetector> failureDetectorFactory = null)
        where TStartup : class
    {
        return new HostBuilder()
            .ConfigureWebHost(webHost => webHost.UseKestrel(options => options.ListenLocalhost(port))
                .ConfigureServices(services =>
                {
                    if (configurator is not null)
                        services.AddSingleton(configurator);

                    if (failureDetectorFactory is not null)
                        services.AddSingleton(failureDetectorFactory);
                    services.AddSingleton<IDatabaseService, SlimDataService>();
                    services.AddHttpClient<IDatabaseService, SlimDataService>()
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler() { AllowAutoRedirect = true });
                })
                .UseStartup<TStartup>()
            )
            .ConfigureHostOptions(static options => options.ShutdownTimeout = DefaultTimeout)
            .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(configuration))
            .ConfigureLogging(builder => builder.AddDebugLogger(port.ToString()).SetMinimumLevel(LogLevel.Debug))
            .JoinCluster()
            .Build();
    }
    private static IRaftHttpCluster GetLocalClusterView(IHost host)
        => host.Services.GetRequiredService<IRaftHttpCluster>();

    public static string GetTemporaryDirectory()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        if(File.Exists(tempDirectory)) {
            return GetTemporaryDirectory();
        }

        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }

    [Fact(Timeout = 20000)]
    public static async Task MessageExchange()
    {
        var config1 = new Dictionary<string, string>
            {
                {"partitioning", "false"},
                {"lowerElectionTimeout", "600" },
                {"upperElectionTimeout", "900" },
                {"publicEndPoint", "http://localhost:3262/" },
                {"coldStart", "false"},
                {"requestTimeout", "00:01:00"},
                {SlimPersistentState.LogLocation, GetTemporaryDirectory()}
            };

        var config2 = new Dictionary<string, string>
            {
                {"partitioning", "false"},
                {"lowerElectionTimeout", "600" },
                {"upperElectionTimeout", "900" },
                {"publicEndPoint", "http://localhost:3263/" },
                {"coldStart", "false"},
                {"standby", "true"},
                {"requestTimeout", "00:01:00"},
                {SlimPersistentState.LogLocation, GetTemporaryDirectory()}
            };

        var listener = new LeaderTracker();
        Startup.ClusterMembers.Add("http://localhost:3262/");
        Startup.ClusterMembers.Add("http://localhost:3263/");
        using var host1 = CreateHost<Startup>(3262, config1, listener);
        await host1.StartAsync();

        using var host2 = CreateHost<Startup>(3263, config2);
        await host2.StartAsync();

        await listener.Task.WaitAsync(DefaultTimeout);
        Assert.Equal(new UriEndPoint(GetLocalClusterView(host1).LocalMemberAddress), listener.Task.Result.EndPoint, EndPointFormatter.UriEndPointComparer);

        Assert.True(await GetLocalClusterView(host1).AddMemberAsync(GetLocalClusterView(host2).LocalMemberAddress));
        await GetLocalClusterView(host2).Readiness.WaitAsync(DefaultTimeout);

        var client = GetLocalClusterView(host1).As<IMessageBus>().Members.First(static s => s.EndPoint is UriEndPoint { Uri: { Port: 3263 } });

        await host1.StopAsync();
        await host2.StopAsync();
    }
}
