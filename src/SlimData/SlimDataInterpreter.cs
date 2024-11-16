using DotNext.Net.Cluster.Consensus.Raft.Commands;
using SlimData.Commands;

namespace SlimData;



public record SlimDataState(
    Dictionary<string, Dictionary<string, string>> Hashsets,
    Dictionary<string, ReadOnlyMemory<byte>> KeyValues,
    Dictionary<string, List<QueueElement>> Queues);

public record QueueElement(
    ReadOnlyMemory<byte> Value,
    string Id,
    long InsertTimeStamp, 
    IList<RetryQueueElement> RetryQueueElements);

public class RetryQueueElement(long startTimeStamp=0, long endTimeStamp=0, int httpCode=0)
{
    public long StartTimeStamp { get; set; } = startTimeStamp;
    public long EndTimeStamp { get;set; } = endTimeStamp;
    public int HttpCode { get;set; } = httpCode;
}

#pragma warning restore CA2252
public class SlimDataInterpreter : CommandInterpreter
{

    public SlimDataState SlimDataState = new(new Dictionary<string, Dictionary<string, string>>(), new Dictionary<string, ReadOnlyMemory<byte>>(), new Dictionary<string, List<QueueElement>>());

    [CommandHandler]
    public ValueTask ListRightPopAsync(ListRightPopCommand addHashSetCommand, CancellationToken token)
    {
        return DoListRightPopAsync(addHashSetCommand, SlimDataState.Queues);
    }
    
    private static readonly List<int> Retries = [2, 6, 10];
    private static readonly int RetryTimeout = 30;
    private static readonly int NumberParralel = 1;

    internal static ValueTask DoListRightPopAsync(ListRightPopCommand addHashSetCommand, Dictionary<string, List<QueueElement>> queues)
    {
        if (queues.TryGetValue(addHashSetCommand.Key, out var queue))
        {
            var nowTicks =addHashSetCommand.NowTicks;
            var queueTimeoutElements = queue.GetQueueTimeoutElement(nowTicks, RetryTimeout);
            foreach (var queueTimeoutElement in queueTimeoutElements)
            {
                var retryQueueElement = queueTimeoutElement.RetryQueueElements[^1];
                retryQueueElement.EndTimeStamp = nowTicks;
                retryQueueElement.HttpCode = 520;
            }
            
            var queueFinishedElements = queue.GetQueueFinishedElement(Retries);
            foreach (var queueFinishedElement in queueFinishedElements)
            {
                queue.Remove(queueFinishedElement);
            }
            
            var queueAvailableElements = queue.GetQueueAvailableElement(Retries, nowTicks, addHashSetCommand.Count);
            foreach (var queueAvailableElement in queueAvailableElements)
            {
                queueAvailableElement.RetryQueueElements.Add(new RetryQueueElement(nowTicks));
            }
        }

        return default;
    }

    [CommandHandler]
    public ValueTask ListLeftPushAsync(ListLeftPushCommand addHashSetCommand, CancellationToken token)
    {
        return DoListLeftPushAsync(addHashSetCommand, SlimDataState.Queues);
    }
    
    internal static ValueTask DoListLeftPushAsync(ListLeftPushCommand addHashSetCommand, Dictionary<string, List<QueueElement>> queues)
    {
        if (queues.TryGetValue(addHashSetCommand.Key, out List<QueueElement>? value))
            value.Add(new QueueElement(addHashSetCommand.Value, Guid.NewGuid().ToString(), DateTime.UtcNow.Ticks,new List<RetryQueueElement>()));
        else
            queues.Add(addHashSetCommand.Key, new List<QueueElement>() {new(addHashSetCommand.Value,Guid.NewGuid().ToString(), DateTime.UtcNow.Ticks,new List<RetryQueueElement>())});

        return default;
    }
    
    [CommandHandler]
    public ValueTask ListSetQueueItemStatusAsync(ListSetQueueItemStatusCommand addHashSetCommand, CancellationToken token)
    {
        return DoListSetQueueItemStatusAsync(addHashSetCommand, SlimDataState.Queues);
    }
    
    internal static ValueTask DoListSetQueueItemStatusAsync(ListSetQueueItemStatusCommand addHashSetCommand, Dictionary<string, List<QueueElement>> queues)
    {
        if (!queues.TryGetValue(addHashSetCommand.Key, out List<QueueElement>? value)) return default;
        var queueElement = value.Find(x => x.Id == addHashSetCommand.Identifier);
        if (queueElement != null)
        {
            var retryQueueElement = queueElement.RetryQueueElements[^1];
            retryQueueElement.EndTimeStamp = DateTime.UtcNow.Ticks;
            retryQueueElement.HttpCode = addHashSetCommand.HttpCode;
        }
        return default;
    }
    
    

    [CommandHandler]
    public ValueTask AddHashSetAsync(AddHashSetCommand addHashSetCommand, CancellationToken token)
    {
        return DoAddHashSetAsync(addHashSetCommand, SlimDataState.Hashsets);
    }
    
    internal static ValueTask DoAddHashSetAsync(AddHashSetCommand addHashSetCommand, Dictionary<string, Dictionary<string, string>> hashsets)
    {
        hashsets[addHashSetCommand.Key] = addHashSetCommand.Value;
        return default;
    }

    [CommandHandler]
    public ValueTask AddKeyValueAsync(AddKeyValueCommand valueCommand, CancellationToken token)
    {
        return DoAddKeyValueAsync(valueCommand, SlimDataState.KeyValues);
    }
    
    internal static ValueTask DoAddKeyValueAsync(AddKeyValueCommand valueCommand, Dictionary<string, ReadOnlyMemory<byte>> keyValues)
    {
        keyValues[valueCommand.Key] = valueCommand.Value;
        return default;
    }

    [CommandHandler(IsSnapshotHandler = true)]
    public ValueTask HandleSnapshotAsync(LogSnapshotCommand command, CancellationToken token)
    {
        SlimDataState = SlimDataState with { KeyValues = command.keysValues };
        SlimDataState = SlimDataState with { Queues = command.queues };
        SlimDataState = SlimDataState with { Hashsets = command.hashsets };
        return default;
    }
    
    internal static ValueTask DoHandleSnapshotAsync(LogSnapshotCommand command, Dictionary<string, ReadOnlyMemory<byte>> keyValues, Dictionary<string, Dictionary<string, string>> hashsets, Dictionary<string, List<QueueElement>>  queues)
    {
        keyValues.Clear();
        foreach (var keyValue in command.keysValues)
        {
            keyValues[keyValue.Key] = keyValue.Value;
        }
            
        queues.Clear();
        foreach (var queue in command.queues)
        {
            queues[queue.Key] = queue.Value;
        }
        
        hashsets.Clear();
        foreach (var hashset in command.hashsets)
        {
            hashsets[hashset.Key] = hashset.Value;
        }
        return default;
    }   
    
    public static CommandInterpreter InitInterpreter(SlimDataState state)   
    {
        ValueTask ListRightPopHandler(ListRightPopCommand command, CancellationToken token) => DoListRightPopAsync(command, state.Queues);
        ValueTask ListLeftPushHandler(ListLeftPushCommand command, CancellationToken token) => DoListLeftPushAsync(command, state.Queues);
        ValueTask AddHashSetHandler(AddHashSetCommand command, CancellationToken token) => DoAddHashSetAsync(command, state.Hashsets);
        ValueTask AddKeyValueHandler(AddKeyValueCommand command, CancellationToken token) => DoAddKeyValueAsync(command, state.KeyValues);
        ValueTask ListSetQueueItemStatusAsync(ListSetQueueItemStatusCommand command, CancellationToken token) => DoListSetQueueItemStatusAsync(command, new Dictionary<string, List<QueueElement>>()  );
        ValueTask SnapshotHandler(LogSnapshotCommand command, CancellationToken token) => DoHandleSnapshotAsync(command, state.KeyValues, state.Hashsets, state.Queues);

        var interpreter = new Builder()
            .Add(ListRightPopCommand.Id, (Func<ListRightPopCommand, CancellationToken, ValueTask>)ListRightPopHandler)
            .Add(ListLeftPushCommand.Id, (Func<ListLeftPushCommand, CancellationToken, ValueTask>)ListLeftPushHandler)
            .Add(AddHashSetCommand.Id, (Func<AddHashSetCommand, CancellationToken, ValueTask>)AddHashSetHandler)
            .Add(AddKeyValueCommand.Id, (Func<AddKeyValueCommand, CancellationToken, ValueTask>)AddKeyValueHandler)
            .Add(ListSetQueueItemStatusCommand.Id, (Func<ListSetQueueItemStatusCommand, CancellationToken, ValueTask>)ListSetQueueItemStatusAsync)
            .Add(LogSnapshotCommand.Id, (Func<LogSnapshotCommand, CancellationToken, ValueTask>)SnapshotHandler, true)
            .Build();

        return interpreter;
    }
    
    
}