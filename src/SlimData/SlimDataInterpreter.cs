using DotNext.Net.Cluster.Consensus.Raft.Commands;

namespace RaftNode;

public record SlimDataState(
    Dictionary<string, Dictionary<string, string>> hashsets,
    Dictionary<string, string> keyValues,
    Dictionary<string, List<string>> queues);

[Command<LogSnapshotCommand>(LogSnapshotCommand.Id)]
[Command<AddKeyValueCommand>(AddKeyValueCommand.Id)]
[Command<ListLeftPushCommand>(ListLeftPushCommand.Id)]
[Command<ListRightPopCommand>(ListRightPopCommand.Id)]
[Command<AddHashSetCommand>(AddHashSetCommand.Id)]
#pragma warning restore CA2252
public class SlimDataInterpreter : CommandInterpreter
{

    public Dictionary<string, Dictionary<string, string>> hashsets = new();
    public Dictionary<string, string> keyValues = new();
    public Dictionary<string, List<string>> queues = new();

    [CommandHandler]
    public ValueTask ListRightPopAsync(ListRightPopCommand addHashSetCommand, CancellationToken token)
    {
        return DoListRightPopAsync(addHashSetCommand, queues);
    }

    internal static ValueTask DoListRightPopAsync(ListRightPopCommand addHashSetCommand, Dictionary<string, List<string>> queues)
    {
        if (queues.TryGetValue(addHashSetCommand.Key, out var queue))
            for (var i = 0; i < addHashSetCommand.Count; i++)
                if (queue.Count > 0)    
                    queue.RemoveAt(0);
                else
                    break;

        return default;
    }

    [CommandHandler]
    public ValueTask ListLeftPushAsync(ListLeftPushCommand addHashSetCommand, CancellationToken token)
    {
        return DoListLeftPushAsync(addHashSetCommand, queues);
    }
    
    internal static ValueTask DoListLeftPushAsync(ListLeftPushCommand addHashSetCommand, Dictionary<string, List<string>> queues)
    {
        if (queues.ContainsKey(addHashSetCommand.Key))
            queues[addHashSetCommand.Key].Add(addHashSetCommand.Value);
        else
            queues.Add(addHashSetCommand.Key, new List<string> { addHashSetCommand.Value });

        return default;
    }

    [CommandHandler]
    public ValueTask AddHashSetAsync(AddHashSetCommand addHashSetCommand, CancellationToken token)
    {
        return DoAddHashSetAsync(addHashSetCommand, hashsets);
    }
    
    internal static ValueTask DoAddHashSetAsync(AddHashSetCommand addHashSetCommand, Dictionary<string, Dictionary<string, string>> hashsets)
    {
        hashsets[addHashSetCommand.Key] = addHashSetCommand.Value;
        return default;
    }

    [CommandHandler]
    public ValueTask AddKeyValueAsync(AddKeyValueCommand valueCommand, CancellationToken token)
    {
        return DoAddKeyValueAsync(valueCommand, keyValues);
    }
    
    internal static ValueTask DoAddKeyValueAsync(AddKeyValueCommand valueCommand, Dictionary<string, string> keyValues)
    {
        keyValues[valueCommand.Key] = valueCommand.Value;
        return default;
    }

    [CommandHandler(IsSnapshotHandler = true)]
    public ValueTask HandleSnapshotAsync(LogSnapshotCommand command, CancellationToken token)
    {
        keyValues = command.keysValues;
        queues = command.queues;
        hashsets = command.hashsets;
        return default;
    }
    
    internal static ValueTask DoHandleSnapshotAsync(LogSnapshotCommand command, Dictionary<string, string> keyValues, Dictionary<string, Dictionary<string, string>> hashsets, Dictionary<string, List<string>> queues)
    {
        keyValues.Clear();
        foreach (var keyValue in keyValues)
        {
            keyValues[keyValue.Key] = keyValue.Value;
        }
            
        queues.Clear();
        foreach (var queue in queues)
        {
            queues[queue.Key] = queue.Value;
        }
        
        hashsets.Clear();
        foreach (var hashset in hashsets)
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

        var interpreter =  new Builder()
            .Add(ListRightPopCommand.Id, (Func<ListRightPopCommand, CancellationToken, ValueTask>)ListRightPopHandler)
            .Add(ListLeftPushCommand.Id, (Func<ListLeftPushCommand, CancellationToken, ValueTask>)ListLeftPushHandler)
            .Add(AddHashSetCommand.Id, (Func<AddHashSetCommand, CancellationToken, ValueTask>)AddHashSetHandler)
            .Add(AddKeyValueCommand.Id, (Func<AddKeyValueCommand, CancellationToken, ValueTask>)AddKeyValueHandler)
            .Add(LogSnapshotCommand.Id, (Func<LogSnapshotCommand, CancellationToken, ValueTask>)SnapshotHandler)
            .Build();

        return interpreter;
    }
    
    
}