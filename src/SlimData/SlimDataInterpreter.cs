using DotNext.Net.Cluster.Consensus.Raft.Commands;
using SlimData.Commands;

namespace SlimData;



public record SlimDataState(
    Dictionary<string, Dictionary<string, string>> Hashsets,
    Dictionary<string, ReadOnlyMemory<byte>> KeyValues,
    Dictionary<string, List<QueueElement>> Queues);

public class QueueElement(
    ReadOnlyMemory<byte> value,
    string id,
    long insertTimeStamp, 
    IList<QueueHttpTryElement> retryQueueElements)
{
    public ReadOnlyMemory<byte> Value { get; } = value;
    public string Id { get; } = id;
    public long InsertTimeStamp { get; } = insertTimeStamp;
    public IList<QueueHttpTryElement> RetryQueueElements { get; } = retryQueueElements;
}

public class QueueHttpTryElement(long startTimeStamp=0, long endTimeStamp=0, int httpCode=0)
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
    
    public static readonly List<int> Retries = [2, 6, 10];
    public static readonly int RetryTimeout = 30;

    internal static ValueTask DoListRightPopAsync(ListRightPopCommand addHashSetCommand, Dictionary<string, List<QueueElement>> queues)
    {
        if (queues.TryGetValue(addHashSetCommand.Key, out var queue))
        {
            var nowTicks =addHashSetCommand.NowTicks;
            var queueTimeoutElements = queue.GetQueueTimeoutElement(nowTicks, RetryTimeout);
            foreach (var queueTimeoutElement in queueTimeoutElements)
            {
                Console.WriteLine("ListRightPopAsync timeout " + queueTimeoutElement.Id + " " + addHashSetCommand.Key);
                var retryQueueElement = queueTimeoutElement.RetryQueueElements[^1];
                retryQueueElement.EndTimeStamp = nowTicks;
                retryQueueElement.HttpCode = 504;
            }
            
            var queueFinishedElements = queue.GetQueueFinishedElement( nowTicks, Retries, RetryTimeout);
            foreach (var queueFinishedElement in queueFinishedElements)
            {
                queue.Remove(queueFinishedElement);
            }
            
            var queueAvailableElements = queue.GetQueueAvailableElement(Retries, nowTicks, addHashSetCommand.Count);
            foreach (var queueAvailableElement in queueAvailableElements)
            {
                queueAvailableElement.RetryQueueElements.Add(new QueueHttpTryElement(nowTicks));
            }

            Console.WriteLine("ListRightPopAsync count " + queues[addHashSetCommand.Key].Count + " " + addHashSetCommand.Key);
        }

        return default;
    }

    [CommandHandler]
    public ValueTask ListLeftPushAsync(ListLeftPushCommand addHashSetCommand, CancellationToken token)
    {
        return DoListLeftPushAsync(addHashSetCommand, SlimDataState.Queues);
    }
    
    internal static ValueTask DoListLeftPushAsync(ListLeftPushCommand listLeftPushCommand, Dictionary<string, List<QueueElement>> queues)
    {
        if (queues.TryGetValue(listLeftPushCommand.Key, out List<QueueElement>? value))
            value.Add(new QueueElement(listLeftPushCommand.Value, listLeftPushCommand.Identifier, DateTime.UtcNow.Ticks,new List<QueueHttpTryElement>()));
        else
            queues.Add(listLeftPushCommand.Key, new List<QueueElement>() {new(listLeftPushCommand.Value,listLeftPushCommand.Identifier, DateTime.UtcNow.Ticks,new List<QueueHttpTryElement>())});
        Console.WriteLine("ListLeftPushAsync count " + queues[listLeftPushCommand.Key].Count);
        Console.WriteLine("ListLeftPushAsync Last element ID " + queues[listLeftPushCommand.Key][^1].Id);
        return default;
    }
    
    [CommandHandler]
    public ValueTask ListSetQueueItemStatusAsync(ListSetQueueItemStatusCommand addHashSetCommand, CancellationToken token)
    {
        return DoListSetQueueItemStatusAsync(addHashSetCommand, SlimDataState.Queues);
    }
    
    internal static ValueTask DoListSetQueueItemStatusAsync(ListSetQueueItemStatusCommand addHashSetCommand, Dictionary<string, List<QueueElement>> queues)
    {
        Console.WriteLine("ListSetQueueItemStatusAsync");
        if (!queues.TryGetValue(addHashSetCommand.Key, out List<QueueElement>? value)) return default;
        Console.WriteLine("ListSetQueueItemStatusAsync 2");
        foreach (var element in value)
        {
            Console.WriteLine("ListSetQueueItemStatusAsync 2 " + element.Id + "==" + addHashSetCommand.Identifier);
        }
        var queueElement = value.FirstOrDefault(x => x.Id == addHashSetCommand.Identifier);
        if (queueElement == null)
        {
            return default;
        }

        Console.WriteLine("ListSetQueueItemStatusAsync 3");
        var retryQueueElement = queueElement.RetryQueueElements[^1];
        retryQueueElement.EndTimeStamp = DateTime.UtcNow.Ticks;
        retryQueueElement.HttpCode = addHashSetCommand.HttpCode;

        if (queueElement.IsFinished(DateTime.UtcNow.Ticks, Retries, RetryTimeout))
        {
            Console.WriteLine("ListSetQueueItemStatusAsync 4 finished");
            value.Remove(queueElement);
        }

        Console.WriteLine("DoListSetQueueItemStatusAsync count " + queues[addHashSetCommand.Key].Count);
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
        DoHandleSnapshotAsync(command, SlimDataState.KeyValues, SlimDataState.Hashsets, SlimDataState.Queues);
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
        ValueTask ListSetQueueItemStatusAsync(ListSetQueueItemStatusCommand command, CancellationToken token) => DoListSetQueueItemStatusAsync(command, state.Queues);
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