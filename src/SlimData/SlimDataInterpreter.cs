using DotNext.Net.Cluster.Consensus.Raft.Commands;
using SlimData.Commands;

namespace SlimData;



public record SlimDataState(
    Dictionary<string, Dictionary<string, string>> hashsets,
    Dictionary<string, ReadOnlyMemory<byte>> keyValues,
    Dictionary<string, List<QueueElement>> queues);

public record QueueElement(
    ReadOnlyMemory<byte> Value,
    string Id,
    long InsertTimeStamp, 
    IList<RetryQueueElement> RetryQueueElements);

public class RetryQueueElement(long StartTimeStamp=0, long EndTimeStamp=0, int HttpCode=0)
{
    public long StartTimeStamp { get; set; } = StartTimeStamp;
    public long EndTimeStamp { get;set; } = EndTimeStamp;
    public int HttpCode { get;set; } = HttpCode;
}

public static class QueueElementExtensions
{
    
    public static IList<QueueElement> GetQueueTimeoutElement(this IList<QueueElement?> element, long nowTicks, int timeout=30)
    {
        var timeoutElements = new List<QueueElement>();
        foreach (var queueElement in element)
        {
            if(queueElement.RetryQueueElements.Count > 0)
            {
                var retryQueueElement = queueElement.RetryQueueElements[^1];
                if (retryQueueElement.EndTimeStamp == 0 &&
                    retryQueueElement.StartTimeStamp + TimeSpan.FromSeconds(timeout).Ticks <= nowTicks)
                {
                    timeoutElements.Add(queueElement);
                }
            }
           
        }
        return timeoutElements;
    }
    
    public static IList<QueueElement> GetQueueRunningElement(this IList<QueueElement?> element, long nowTicks, int timeout=30)
    {
        var runningElement = new List<QueueElement>();
        foreach (var queueElement in element)
        {
            if(queueElement.RetryQueueElements.Count > 0)
            {
                var retryQueueElement = queueElement.RetryQueueElements[^1];
                if (retryQueueElement.EndTimeStamp == 0 &&
                    retryQueueElement.StartTimeStamp + TimeSpan.FromSeconds(timeout).Ticks > nowTicks)
                {
                    runningElement.Add(queueElement);
                }
            }
           
        }
        return runningElement;
    }
    
    public static IList<QueueElement> GetQueueAvailableElement(this IList<QueueElement?> element, List<int> retries, long nowTicks, int maximum)
    {
        var currentCount = 0;
        var availableElements = new List<QueueElement>();
        foreach (var queueElement in element)
        {
            if (currentCount == maximum)
            {
                return availableElements;
            }
            var count = queueElement.RetryQueueElements.Count;
            if (count == 0)
            {
                availableElements.Add(queueElement);
                currentCount++;
            }
            else
            {
                var retryQueueElement = queueElement.RetryQueueElements[^1];
                if (retryQueueElement.HttpCode >= 400 
                    && retries.Count <= count 
                    && retryQueueElement.EndTimeStamp != 0 
                    && nowTicks > retryQueueElement.EndTimeStamp + TimeSpan.FromSeconds(retries[count - 1]).Ticks 
                    )
                {
                    availableElements.Add(queueElement);
                    currentCount++;
                }
            }
           
        }
        return availableElements;
    }
    
    public static IList<QueueElement> GetQueueFinishedElement(this IList<QueueElement?> element, List<int> retries)
    {
        var runningElement = new List<QueueElement>();
        foreach (var queueElement in element)
        {
            var count = queueElement.RetryQueueElements.Count;
            if(count > 0)
            {
                var retryQueueElement = queueElement.RetryQueueElements[^1];
                if (retryQueueElement.HttpCode is >= 200 and < 400 || retries.Count <= count)
                {
                    runningElement.Add(queueElement);
                }
            }
           
        }
        return runningElement;
    }

}


#pragma warning restore CA2252
public class SlimDataInterpreter : CommandInterpreter
{

    public SlimDataState SlimDataState = new(new Dictionary<string, Dictionary<string, string>>(), new Dictionary<string, ReadOnlyMemory<byte>>(), new Dictionary<string, List<QueueElement>>());

    [CommandHandler]
    public ValueTask ListRightPopAsync(ListRightPopCommand addHashSetCommand, CancellationToken token)
    {
        return DoListRightPopAsync(addHashSetCommand, SlimDataState.queues);
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
        return DoListLeftPushAsync(addHashSetCommand, SlimDataState.queues);
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
    public ValueTask AddHashSetAsync(AddHashSetCommand addHashSetCommand, CancellationToken token)
    {
        return DoAddHashSetAsync(addHashSetCommand, SlimDataState.hashsets);
    }
    
    internal static ValueTask DoAddHashSetAsync(AddHashSetCommand addHashSetCommand, Dictionary<string, Dictionary<string, string>> hashsets)
    {
        hashsets[addHashSetCommand.Key] = addHashSetCommand.Value;
        return default;
    }

    [CommandHandler]
    public ValueTask AddKeyValueAsync(AddKeyValueCommand valueCommand, CancellationToken token)
    {
        return DoAddKeyValueAsync(valueCommand, SlimDataState.keyValues);
    }
    
    internal static ValueTask DoAddKeyValueAsync(AddKeyValueCommand valueCommand, Dictionary<string, ReadOnlyMemory<byte>> keyValues)
    {
        keyValues[valueCommand.Key] = valueCommand.Value;
        return default;
    }

    [CommandHandler(IsSnapshotHandler = true)]
    public ValueTask HandleSnapshotAsync(LogSnapshotCommand command, CancellationToken token)
    {
        SlimDataState = SlimDataState with { keyValues = command.keysValues };
        SlimDataState = SlimDataState with { queues = command.queues };
        SlimDataState = SlimDataState with { hashsets = command.hashsets };
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
        ValueTask ListRightPopHandler(ListRightPopCommand command, CancellationToken token) => DoListRightPopAsync(command, state.queues);
        ValueTask ListLeftPushHandler(ListLeftPushCommand command, CancellationToken token) => DoListLeftPushAsync(command, state.queues);
        ValueTask AddHashSetHandler(AddHashSetCommand command, CancellationToken token) => DoAddHashSetAsync(command, state.hashsets);
        ValueTask AddKeyValueHandler(AddKeyValueCommand command, CancellationToken token) => DoAddKeyValueAsync(command, state.keyValues);
        ValueTask SnapshotHandler(LogSnapshotCommand command, CancellationToken token) => DoHandleSnapshotAsync(command, state.keyValues, state.hashsets, state.queues);

        var interpreter = new Builder()
            .Add(ListRightPopCommand.Id, (Func<ListRightPopCommand, CancellationToken, ValueTask>)ListRightPopHandler)
            .Add(ListLeftPushCommand.Id, (Func<ListLeftPushCommand, CancellationToken, ValueTask>)ListLeftPushHandler)
            .Add(AddHashSetCommand.Id, (Func<AddHashSetCommand, CancellationToken, ValueTask>)AddHashSetHandler)
            .Add(AddKeyValueCommand.Id, (Func<AddKeyValueCommand, CancellationToken, ValueTask>)AddKeyValueHandler)
            .Add(LogSnapshotCommand.Id, (Func<LogSnapshotCommand, CancellationToken, ValueTask>)SnapshotHandler, true)
            .Build();

        return interpreter;
    }
    
    
}