using DotNext.Net.Cluster.Consensus.Raft.Commands;
using Newtonsoft.Json;

namespace RaftNode;

[Command<LogSnapshotCommand>(LogSnapshotCommand.Id)]
[Command<AddKeyValueCommand>(AddKeyValueCommand.Id)]
[Command<ListLeftPushCommand>(ListLeftPushCommand.Id)]
[Command<ListRightPopCommand>(ListRightPopCommand.Id)]
[Command<AddHashSetCommand>(AddHashSetCommand.Id)]
#pragma warning restore CA2252
public class SlimDataInterpreter : CommandInterpreter
{
    private readonly string prefix;
    public IDictionary<string, string> keyValues = new Dictionary<string, string>();
    public IDictionary<string, List<string>> queues = new Dictionary<string, List<string>>();
    public IDictionary<string, Dictionary<string,string>> hashsets = new Dictionary<string, Dictionary<string, string>>();

    public SlimDataInterpreter(string prefix)
    {
        this.prefix = prefix;
    }
    
    [CommandHandler]
    public async ValueTask ListRightPopAsync(ListRightPopCommand addHashSetCommand, CancellationToken token)
    {
        if (queues.ContainsKey(addHashSetCommand.Key))
        {
            var queue = queues[addHashSetCommand.Key];    
            if (queue.Count > 0)
            {
                queue.RemoveAt(0);
            }
        }
    }
    
    [CommandHandler]
    public async ValueTask ListLeftPushAsync(ListLeftPushCommand addHashSetCommand, CancellationToken token)
    {
        if (queues.ContainsKey(addHashSetCommand.Key))
        {
            queues[addHashSetCommand.Key].Add(addHashSetCommand.Value);    
        }
        else
        {
            queues[addHashSetCommand.Key] = new List<string> {addHashSetCommand.Value}; 
        }
    }
    
    [CommandHandler]
    public async ValueTask AddHashSetAsync(AddHashSetCommand addHashSetCommand, CancellationToken token)
    {
        if (hashsets.ContainsKey(addHashSetCommand.Key))
        {
            hashsets.Add(addHashSetCommand.Key, addHashSetCommand.Value);    
        }
        else
        {
            hashsets[addHashSetCommand.Key] = addHashSetCommand.Value; 
        }
    }
    
    [CommandHandler]
    public async ValueTask AddKeyValueAsync(AddKeyValueCommand valueCommand, CancellationToken token)
    {
        if (keyValues.ContainsKey(valueCommand.Key))
        {
            keyValues.Add(valueCommand.Key, valueCommand.Value);    
        }
        else
        {
            keyValues[valueCommand.Key] = valueCommand.Value; 
        }
        Console.WriteLine($"{prefix}>SlimDataInterpreter>Handling valueCommand SubtractAsync :{valueCommand.Value}");
    }
    
    [CommandHandler(IsSnapshotHandler = true)]
    public async ValueTask HandleSnapshotAsync(LogSnapshotCommand command, CancellationToken token)
    {
        Console.WriteLine($"{prefix}>SlimDataInterpreter>Handling snapshot HandleSnapshotAsync" + JsonConvert.SerializeObject(command.keysValues) );
        keyValues = command.keysValues;
        queues = command.queues;
        hashsets = command.hashsets;
    }
    
}