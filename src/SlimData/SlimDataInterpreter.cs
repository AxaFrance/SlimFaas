﻿using DotNext.Net.Cluster.Consensus.Raft.Commands;
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
    int httpTimeout,
    List<int> timeoutRetries,
    IList<QueueHttpTryElement> retryQueueElements,
    List<int> httpStatusRetries
    )
{
    public ReadOnlyMemory<byte> Value { get; } = value;
    public string Id { get; } = id;
    public long InsertTimeStamp { get; } = insertTimeStamp;
    
    public List<int> TimeoutRetries { get; } = timeoutRetries;

    public int HttpTimeout { get; } = httpTimeout;
    public IList<QueueHttpTryElement> RetryQueueElements { get; } = retryQueueElements;
    
    public List<int> HttpStatusRetries { get; } = httpStatusRetries;
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
        Console.WriteLine("DoListRightPopAsync Key " +  addHashSetCommand.Key);
        if (queues.TryGetValue(addHashSetCommand.Key, out var queue))
        {
            var nowTicks =addHashSetCommand.NowTicks;
            var queueTimeoutElements = queue.GetQueueTimeoutElement(nowTicks);
            foreach (var queueTimeoutElement in queueTimeoutElements)
            {
                var retryQueueElement = queueTimeoutElement.RetryQueueElements[^1];
                retryQueueElement.EndTimeStamp = nowTicks;
                retryQueueElement.HttpCode = 504;
            }
            
            var queueFinishedElements = queue.GetQueueFinishedElement(nowTicks);
            foreach (var queueFinishedElement in queueFinishedElements)
            {
                queue.Remove(queueFinishedElement);
            }
            
            var queueAvailableElements = queue.GetQueueAvailableElement(nowTicks, addHashSetCommand.Count);
            foreach (var queueAvailableElement in queueAvailableElements)
            {
                queueAvailableElement.RetryQueueElements.Add(new QueueHttpTryElement(nowTicks));
            }

            // print all queue elements
            foreach (var queueElemen in queue)
            {
                Console.WriteLine("DoListRightPopAsync QueueElement Id " +  queueElemen.Id);
                Console.WriteLine("DoListRightPopAsync QueueElement Value " +  queueElemen.Value);
                Console.WriteLine("DoListRightPopAsync QueueElement InsertTimeStamp " +  queueElemen.InsertTimeStamp);
                foreach (var retryQueueElemen in queueElemen.RetryQueueElements)
                {
                    Console.WriteLine("DoListRightPopAsync QueueElement RetryQueueElement StartTimeStamp " +  retryQueueElemen.StartTimeStamp);
                    Console.WriteLine("DoListRightPopAsync QueueElement RetryQueueElement EndTimeStamp " +  retryQueueElemen.EndTimeStamp);
                    Console.WriteLine("DoListRightPopAsync QueueElement RetryQueueElement HttpCode " +  retryQueueElemen.HttpCode);
                }
            }
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
            value.Add(new QueueElement(listLeftPushCommand.Value, listLeftPushCommand.Identifier, listLeftPushCommand.NowTicks, listLeftPushCommand.RetryTimeout, listLeftPushCommand.Retries,new List<QueueHttpTryElement>(), listLeftPushCommand.HttpStatusCodesWorthRetrying));
        else
            queues.Add(listLeftPushCommand.Key, new List<QueueElement>() {new(listLeftPushCommand.Value,listLeftPushCommand.Identifier, listLeftPushCommand.NowTicks, listLeftPushCommand.RetryTimeout, listLeftPushCommand.Retries,new List<QueueHttpTryElement>(), listLeftPushCommand.HttpStatusCodesWorthRetrying)});
        // print all queue elements
        Console.WriteLine("DoListLeftPushAsync key " +  listLeftPushCommand.Key + " Identifier " + listLeftPushCommand.Identifier);
        var elements = queues[listLeftPushCommand.Key];
        foreach (var queueElemen in elements)
        {
            Console.WriteLine("DoListLeftPushAsync QueueElement Id " +  queueElemen.Id);
            Console.WriteLine("DoListLeftPushAsync QueueElement Value " +  queueElemen.Value);
            Console.WriteLine("DoListLeftPushAsync QueueElement InsertTimeStamp " +  queueElemen.InsertTimeStamp);
            foreach (var retryQueueElemen in queueElemen.RetryQueueElements)
            {
                Console.WriteLine("DoListLeftPushAsync QueueElement RetryQueueElement StartTimeStamp " +  retryQueueElemen.StartTimeStamp);
                Console.WriteLine("DoListLeftPushAsync QueueElement RetryQueueElement EndTimeStamp " +  retryQueueElemen.EndTimeStamp);
                Console.WriteLine("DoListLeftPushAsync QueueElement RetryQueueElement HttpCode " +  retryQueueElemen.HttpCode);
            }
        }
        return default;
    }
    
    [CommandHandler]
    public ValueTask ListCallbackAsync(ListCallbackCommand addHashSetCommand, CancellationToken token)
    {
        return DoListCallbackAsync(addHashSetCommand, SlimDataState.Queues);
    }
    
    internal static ValueTask DoListCallbackAsync(ListCallbackCommand listCallbackCommand, Dictionary<string, List<QueueElement>> queues)
    {
        if (!queues.TryGetValue(listCallbackCommand.Key, out List<QueueElement>? value)) return default;

        Console.WriteLine("DoListCallbackAsync QueueKey " +  listCallbackCommand.Key + " Identifier " + listCallbackCommand.Identifier);
        var queueElement = value.FirstOrDefault(x => x.Id == listCallbackCommand.Identifier);
        if (queueElement == null)
        {
            return default;
        }
        var retryQueueElement = queueElement.RetryQueueElements[^1];
        retryQueueElement.EndTimeStamp = listCallbackCommand.NowTicks;
        retryQueueElement.HttpCode = listCallbackCommand.HttpCode;

        if (queueElement.IsFinished(listCallbackCommand.NowTicks))
        {
            value.Remove(queueElement);
        }
       
        // print all queue elements
        foreach (var queueElemen in value)
        {
            Console.WriteLine("DoListCallbackAsync QueueElement Id " +  queueElemen.Id);
            Console.WriteLine("DoListCallbackAsync QueueElement Value " +  queueElemen.Value);
            Console.WriteLine("DoListCallbackAsync QueueElement InsertTimeStamp " +  queueElemen.InsertTimeStamp);
            foreach (var retryQueueElemen in queueElemen.RetryQueueElements)
            {
                Console.WriteLine("DoListCallbackAsync QueueElement RetryQueueElement StartTimeStamp " +  retryQueueElemen.StartTimeStamp);
                Console.WriteLine("DoListCallbackAsync QueueElement RetryQueueElement EndTimeStamp " +  retryQueueElemen.EndTimeStamp);
                Console.WriteLine("DoListCallbackAsync QueueElement RetryQueueElement HttpCode " +  retryQueueElemen.HttpCode);
            }
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
        // Print all data from queues
        foreach (var queue in command.queues)
        {
            Console.WriteLine("SnapshotCommand Queue Key " +  queue.Key);
            foreach (var element in queue.Value)
            {
                Console.WriteLine("SnapshotCommand QueueElement Id " +  element.Id);
                Console.WriteLine("SnapshotCommand QueueElement Value " +  element.Value);
                Console.WriteLine("SnapshotCommand QueueElement InsertTimeStamp " +  element.InsertTimeStamp);
                foreach (var retryQueueElement in element.RetryQueueElements)
                {
                    Console.WriteLine("SnapshotCommand QueueElement RetryQueueElement StartTimeStamp " +  retryQueueElement.StartTimeStamp);
                    Console.WriteLine("SnapshotCommand QueueElement RetryQueueElement EndTimeStamp " +  retryQueueElement.EndTimeStamp);
                    Console.WriteLine("SnapshotCommand QueueElement RetryQueueElement HttpCode " +  retryQueueElement.HttpCode);
                }
            }
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
        ValueTask ListSetQueueItemStatusAsync(ListCallbackCommand command, CancellationToken token) => DoListCallbackAsync(command, state.Queues);
        ValueTask SnapshotHandler(LogSnapshotCommand command, CancellationToken token) => DoHandleSnapshotAsync(command, state.KeyValues, state.Hashsets, state.Queues);

        var interpreter = new Builder()
            .Add(ListRightPopCommand.Id, (Func<ListRightPopCommand, CancellationToken, ValueTask>)ListRightPopHandler)
            .Add(ListLeftPushCommand.Id, (Func<ListLeftPushCommand, CancellationToken, ValueTask>)ListLeftPushHandler)
            .Add(AddHashSetCommand.Id, (Func<AddHashSetCommand, CancellationToken, ValueTask>)AddHashSetHandler)
            .Add(AddKeyValueCommand.Id, (Func<AddKeyValueCommand, CancellationToken, ValueTask>)AddKeyValueHandler)
            .Add(ListCallbackCommand.Id, (Func<ListCallbackCommand, CancellationToken, ValueTask>)ListSetQueueItemStatusAsync)
            .Add(LogSnapshotCommand.Id, (Func<LogSnapshotCommand, CancellationToken, ValueTask>)SnapshotHandler, true)
            .Build();

        return interpreter;
    }
    
    
}