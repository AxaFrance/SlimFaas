using DotNext.Net.Cluster.Consensus.Raft.Commands;

namespace RaftNode;

[Command<LogSnapshotCommand>(LogSnapshotCommand.Id)]
[Command<AddKeyValueCommand>(AddKeyValueCommand.Id)]
[Command<ListLeftPushCommand>(ListLeftPushCommand.Id)]
[Command<ListRightPopCommand>(ListRightPopCommand.Id)]
[Command<AddHashSetCommand>(AddHashSetCommand.Id)]
#pragma warning restore CA2252
public class SlimDataInterpreter(string prefix) : CommandInterpreter
{
    private readonly string prefix = prefix;

    public IDictionary<string, Dictionary<string, string>> hashsets =
        new Dictionary<string, Dictionary<string, string>>();

    public IDictionary<string, string> keyValues = new Dictionary<string, string>();
    public IDictionary<string, List<string>> queues = new Dictionary<string, List<string>>();

    [CommandHandler]
    public ValueTask ListRightPopAsync(ListRightPopCommand addHashSetCommand, CancellationToken token)
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
        if (queues.ContainsKey(addHashSetCommand.Key))
            queues[addHashSetCommand.Key].Add(addHashSetCommand.Value);
        else
            queues.Add(addHashSetCommand.Key, new List<string> { addHashSetCommand.Value });

        return default;
    }

    [CommandHandler]
    public ValueTask AddHashSetAsync(AddHashSetCommand addHashSetCommand, CancellationToken token)
    {
        hashsets[addHashSetCommand.Key] = addHashSetCommand.Value;
        return default;
    }

    [CommandHandler]
    public ValueTask AddKeyValueAsync(AddKeyValueCommand valueCommand, CancellationToken token)
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
}