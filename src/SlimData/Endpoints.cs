using System.Text.Json;
using DotNext;
using DotNext.Net.Cluster.Consensus.Raft;
using RaftNode;

namespace SlimData;

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

    public static async Task RespondAsync(HttpContext context, RespondDelegate respondDelegate)
    {
        var slimDataInfo = context.RequestServices.GetRequiredService<SlimDataInfo>();
        if (context.Request.Host.Port != slimDataInfo.Port)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var cluster = context.RequestServices.GetRequiredService<IRaftCluster>();
        var provider = context.RequestServices.GetRequiredService<SlimPersistentState>();
        var source =
            CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,
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

    public static Task AddHashSet(HttpContext context)
    {
        return RespondAsync(context, async (cluster, provider, source) =>
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
                await context.Response.WriteAsync("Key ______key_____ is empty", context.RequestAborted);
                return;
            }

            var logEntry =
                provider.interpreter.CreateLogEntry(
                    new AddHashSetCommand { Key = key, Value = dictionary }, cluster.Term);
            await provider.AppendAsync(logEntry, source.Token);
            await provider.CommitAsync(source.Token);
        });
    }

    public static Task ListRigthPop(HttpContext context)
    {
        return RespondAsync(context, async (cluster, provider, source) =>
        {
            var form = await context.Request.ReadFormAsync(source.Token);

            var key = string.Empty;
            var value = string.Empty;
            foreach (var formData in form)
            {
                key = formData.Key;
                value = formData.Value.ToString();
                break;
            }

            if (string.IsNullOrEmpty(key) || !int.TryParse(value, out var count))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Key key is empty or value is not a number",
                    context.RequestAborted);
                return;
            }

            await cluster.ApplyReadBarrierAsync(context.RequestAborted);

            var values = new ListString();
            var queues = ((ISupplier<SupplierPayload>)provider).Invoke().Queues;
            if (queues.ContainsKey(key))
            {
                var queue = ((ISupplier<SupplierPayload>)provider).Invoke().Queues[key];
                for (var i = 0; i < count; i++)
                {
                    if (queue.Count <= i) break;

                    values.Add(queue[i]);
                }

                await context.Response.WriteAsync(
                    JsonSerializer.Serialize(values, ListStringSerializerContext.Default.ListString),
                    context.RequestAborted);
                var logEntry =
                    provider.interpreter.CreateLogEntry(
                        new ListRightPopCommand { Key = key, Count = count },
                        cluster.Term);
                await provider.AppendAsync(logEntry, source.Token);
                await provider.CommitAsync(source.Token);
            }
        });
    }

    public static Task ListLeftPush(HttpContext context)
    {
        return RespondAsync(context, async (cluster, provider, source) =>
        {
            var form = await context.Request.ReadFormAsync(source.Token);

            var key = string.Empty;
            var value = string.Empty;

            foreach (var keyValue in form)
            {
                key = keyValue.Key;
                value = keyValue.Value.ToString();
                break;
            }

            if (string.IsNullOrEmpty(key))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("not data found", context.RequestAborted);
                return;
            }

            var logEntry =
                provider.interpreter.CreateLogEntry(new ListLeftPushCommand { Key = key, Value = value },
                    cluster.Term);
            await provider.AppendAsync(logEntry, source.Token);
            await provider.CommitAsync(source.Token);
        });
    }

    public static Task AddKeyValue(HttpContext context)
    {
        return RespondAsync(context, async (cluster, provider, source) =>
        {
            var form = await context.Request.ReadFormAsync(source.Token);

            var key = string.Empty;
            var value = string.Empty;

            foreach (var keyValue in form)
            {
                key = keyValue.Key;
                value = keyValue.Value.ToString();
                break;
            }

            if (string.IsNullOrEmpty(key))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("not data found", context.RequestAborted);
                return;
            }

            var logEntry =
                provider.interpreter.CreateLogEntry(new AddKeyValueCommand { Key = key, Value = value },
                    cluster.Term);
            await provider.AppendAsync(logEntry, source.Token);
            await provider.CommitAsync(source.Token);
        });
    }
}