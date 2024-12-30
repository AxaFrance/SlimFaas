﻿using DotNext;
using DotNext.Net.Cluster.Consensus.Raft;
using MemoryPack;
using SlimData.Commands;

namespace SlimData;

[MemoryPackable]
public partial record ListLeftPushInput(byte[] Value, byte[] RetryInformation);

[MemoryPackable]
public partial record RetryInformation(List<int> Retries, int RetryTimeoutSeconds, List<int> HttpStatusRetries);

[MemoryPackable]
public partial record QueueItemStatus(string Id="", int HttpCode=0);

[MemoryPackable]
public partial record ListQueueItemStatus
{
    public List<QueueItemStatus>? Items { get; set; }
}

public class Endpoints
{
    public delegate Task RespondDelegate(IRaftCluster cluster, SlimPersistentState provider,
        CancellationTokenSource? source);

    public static Task RedirectToLeaderAsync(HttpContext context)
    {
        var cluster = context.RequestServices.GetRequiredService<IRaftCluster>();
        return context.Response.WriteAsync(
            $"Leader address is {cluster.Leader?.EndPoint}. Current address is {context.Connection.LocalIpAddress}:{context.Connection.LocalPort}",
            context.RequestAborted);
    }

    public static async Task DoAsync(HttpContext context, RespondDelegate respondDelegate)
    {
        var slimDataInfo = context.RequestServices.GetRequiredService<SlimDataInfo>();
        if (context.Request.Host.Port != slimDataInfo.Port)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var cluster = context.RequestServices.GetRequiredService<IRaftCluster>();
        var provider = context.RequestServices.GetRequiredService<SlimPersistentState>();
        var source = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,
                cluster.LeadershipToken);
        try
        {
            await respondDelegate(cluster, provider, source);
        }
        catch (Exception e)
        {
            Console.WriteLine("Unexpected error {0}", e);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
        finally
        {
            source?.Dispose();
        }
    }

    public static Task AddHashSetAsync(HttpContext context)
    {
        return DoAsync(context, async (cluster, provider, source) =>
        {
            var form = await context.Request.ReadFormAsync(source.Token);

            var key = string.Empty;
            var dictionary = new Dictionary<string, string>();
            foreach (var formData in form)
                if (formData.Key == "______key_____")
                    key = formData.Value.ToString();
                else
                    dictionary[formData.Key] = formData.Value.ToString();

            if (string.IsNullOrEmpty(key))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("GetKeyValue ______key_____ is empty", context.RequestAborted);
                return;
            }

            await AddHashSetCommand(provider, key, dictionary, cluster, source);
        });
    }

    public static async Task AddHashSetCommand(SlimPersistentState provider, string key, Dictionary<string, string> dictionary,
        IRaftCluster cluster, CancellationTokenSource source)
    {
        var logEntry =
            provider.Interpreter.CreateLogEntry(
                new AddHashSetCommand { Key = key, Value = dictionary }, cluster.Term);
        await cluster.ReplicateAsync(logEntry, source.Token);
    }

    public static Task ListRightPopAsync(HttpContext context)
    {
        return DoAsync(context, async (cluster, provider, source) =>
        {
            var form = await context.Request.ReadFormAsync(source.Token);

            var (key, value) = GetKeyValue(form);

            if (string.IsNullOrEmpty(key) || !int.TryParse(value, out var count))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("GetKeyValue key is empty or value is not a number", context.RequestAborted);
                return;
            }
            
            var values = await ListRightPopCommand(provider, key, count, cluster, source);
            var bin = MemoryPackSerializer.Serialize(values);
            await context.Response.Body.WriteAsync(bin, context.RequestAborted);
        });
    }
    
    private static readonly IDictionary<string,SemaphoreSlim> SemaphoreSlims = new Dictionary<string, SemaphoreSlim>();
    public static async Task<ListItems> ListRightPopCommand(SlimPersistentState provider, string key, int count, IRaftCluster cluster,
        CancellationTokenSource source)
    {
        var values = new ListItems();
        values.Items = new List<QueueData>();

        if(SemaphoreSlims.TryGetValue(key, out var semaphoreSlim))
        {
            await semaphoreSlim.WaitAsync();
        }
        else
        {
            SemaphoreSlims[key] = new SemaphoreSlim(1, 1);
            await SemaphoreSlims[key].WaitAsync();
        }
        try
        {
            while (cluster.TryGetLeaseToken(out var leaseToken) && leaseToken.IsCancellationRequested)
            {
                Console.WriteLine("Master node is waiting for lease token");
                await Task.Delay(10);
            }
            var nowTicks = DateTime.UtcNow.Ticks;
            var queues = ((ISupplier<SlimDataPayload>)provider).Invoke().Queues;
            if (queues.TryGetValue(key, out var queue))
            {
                var queueElements = queue.GetQueueAvailableElement(nowTicks, count);
                foreach (var queueElement in queueElements)
                {
                    values.Items.Add(new QueueData(queueElement.Id ,queueElement.Value.ToArray()));
                }
                
                var logEntry =
                    provider.Interpreter.CreateLogEntry(
                        new ListRightPopCommand { Key = key, Count = count, NowTicks = nowTicks },
                        cluster.Term);
                await cluster.ReplicateAsync(logEntry, source.Token);
            }
        }
        finally
        {
            SemaphoreSlims[key].Release();
        }
        return values;
        
    }


    public static Task ListLeftPushAsync(HttpContext context)
    {
        return DoAsync(context, async (cluster, provider, source) =>
        {
            context.Request.Query.TryGetValue("key", out var key);
            if (string.IsNullOrEmpty(key))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("not data found", context.RequestAborted);
                return;
            }
            
            var inputStream = context.Request.Body;
            await using var memoryStream = new MemoryStream();
            await inputStream.CopyToAsync(memoryStream, source.Token);
            var value = memoryStream.ToArray();
            await ListLeftPushCommand(provider, key, value, cluster, source);
        });
    }
    
    public static async Task ListLeftPushCommand(SlimPersistentState provider, string key, byte[] value,
        IRaftCluster cluster, CancellationTokenSource source)
    {
        ListLeftPushInput input = MemoryPackSerializer.Deserialize<ListLeftPushInput>(value);
        RetryInformation retryInformation = MemoryPackSerializer.Deserialize<RetryInformation>(input.RetryInformation);
        var logEntry =
            provider.Interpreter.CreateLogEntry(new ListLeftPushCommand { Key = key, 
                    Identifier = Guid.NewGuid().ToString(), 
                    Value = input.Value, 
                    NowTicks = DateTime.UtcNow.Ticks,
                    Retries = retryInformation.Retries,
                    RetryTimeout = retryInformation.RetryTimeoutSeconds,
                    HttpStatusCodesWorthRetrying = retryInformation.HttpStatusRetries
                },
                cluster.Term);
        await cluster.ReplicateAsync(logEntry, source.Token);
    }
    
    public static Task ListCallbackAsync(HttpContext context)
    {
        return DoAsync(context, async (cluster, provider, source) =>
        {
            context.Request.Query.TryGetValue("key", out var key);
            if (string.IsNullOrEmpty(key))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("not data found", context.RequestAborted);
                return;
            }
            
            var inputStream = context.Request.Body;
            await using var memoryStream = new MemoryStream();
            await inputStream.CopyToAsync(memoryStream, source.Token);
            var value = memoryStream.ToArray();
            var list = MemoryPackSerializer.Deserialize<ListQueueItemStatus>(value);
            await ListCallbackCommandAsync(provider, key, list, cluster, source);
        });
    }

    public static async Task ListCallbackCommandAsync(SlimPersistentState provider, string key, ListQueueItemStatus list, IRaftCluster cluster, CancellationTokenSource source)
    {
        if (list.Items == null)
        {
            return;
        }

        foreach (var queueItemStatus in list.Items)
        {
            var logEntry =
                provider.Interpreter.CreateLogEntry(new ListCallbackCommand
                    {
                        Identifier = queueItemStatus.Id,
                        Key = key,
                        HttpCode = queueItemStatus.HttpCode,
                        NowTicks = DateTime.UtcNow.Ticks
                    },
                    cluster.Term);
            await cluster.ReplicateAsync(logEntry, source.Token);
        }
    }

    private static (string key, string value) GetKeyValue(IFormCollection form)
    {
        var key = string.Empty;
        var value = string.Empty;

        if (form.Count > 0)
        {
            var keyValue = form.First();
            key = keyValue.Key;
            value = keyValue.Value.ToString();
        }

        return (key, value);
    }

    public static Task AddKeyValueAsync(HttpContext context)
    {
        return DoAsync(context, async (cluster, provider, source) =>
        {
            context.Request.Query.TryGetValue("key", out var key);
            if (string.IsNullOrEmpty(key))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("not data found", context.RequestAborted);
                return;
            }
            var inputStream = context.Request.Body;
            await using var memoryStream = new MemoryStream();
            await inputStream.CopyToAsync(memoryStream, source.Token);
            var value = memoryStream.ToArray();
            await AddKeyValueCommand(provider, key, value, cluster, source);
        });
    }

    public static async Task AddKeyValueCommand(SlimPersistentState provider, string key, byte[] value,
        IRaftCluster cluster, CancellationTokenSource source)
    {
        var logEntry =
            provider.Interpreter.CreateLogEntry(new AddKeyValueCommand { Key = key, Value = value },
                cluster.Term);
        await cluster.ReplicateAsync(logEntry, source.Token);
    }
}