using System.Text;
using DotNext.IO;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Runtime.Serialization;
using DotNext.Net.Cluster.Consensus.Raft.Commands;
using DotNext.Text;
using Newtonsoft.Json;

namespace RaftNode;


public struct JsonPayload
{
    public int X { get; set; }
    public int Y { get; set; }
    public string Message { get; set; }
}

public struct AddHashSetCommand : ISerializable<AddHashSetCommand>
{
    public const int Id = 4;

    public string Key { get; set; }
    public string Value { get; set; }

    long? IDataTransferObject.Length => sizeof(int) + sizeof(int);

    public async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token)
        where TWriter : notnull, IAsyncBinaryWriter
    {
        var command = this;
        await writer.WriteStringAsync(command.Key.AsMemory(), new EncodingContext(Encoding.UTF8, false), LengthFormat.Plain, token);
        await writer.WriteStringAsync(command.Value.AsMemory(), new EncodingContext(Encoding.UTF8, false), LengthFormat.Plain, token);
    }

#pragma warning disable CA2252
    public static async ValueTask<AddHashSetCommand> ReadFromAsync<TReader>(TReader reader, CancellationToken token)
#pragma warning restore CA2252
        where TReader : notnull, IAsyncBinaryReader
    {
        return new AddHashSetCommand
        {
            Key = await reader.ReadStringAsync(LengthFormat.Plain, new DecodingContext(Encoding.UTF8, false), token),
            Value = await reader.ReadStringAsync(LengthFormat.Plain, new DecodingContext(Encoding.UTF8, false), token),
        };
    }
}


public struct AddKeyValueCommand : ISerializable<AddKeyValueCommand>
{
    public const int Id = 3;

    public string Key { get; set; }
    public string Value { get; set; }

    long? IDataTransferObject.Length => sizeof(int) + sizeof(int);

    public async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token)
        where TWriter : notnull, IAsyncBinaryWriter
    {
        var command = this;
        await writer.WriteStringAsync(command.Key.AsMemory(), new EncodingContext(Encoding.UTF8, false), LengthFormat.Plain, token);
        await writer.WriteStringAsync(command.Value.AsMemory(), new EncodingContext(Encoding.UTF8, false), LengthFormat.Plain, token);
    }

#pragma warning disable CA2252
    public static async ValueTask<AddKeyValueCommand> ReadFromAsync<TReader>(TReader reader, CancellationToken token)
#pragma warning restore CA2252
        where TReader : notnull, IAsyncBinaryReader
    {
        return new AddKeyValueCommand
        {
            Key = await reader.ReadStringAsync(LengthFormat.Plain, new DecodingContext(Encoding.UTF8, false), token),
            Value = await reader.ReadStringAsync(LengthFormat.Plain, new DecodingContext(Encoding.UTF8, false), token),
        };
    }
}

public struct SubtractCommand : ISerializable<SubtractCommand>
{
    public const int Id = 0;

    public int X { get; set; }
    public int Y { get; set; }

    long? IDataTransferObject.Length => sizeof(int) + sizeof(int);

    public async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token)
        where TWriter : notnull, IAsyncBinaryWriter
    {
        var command = this;
        await writer.WriteInt32Async(command.X, true, token);
        await writer.WriteInt32Async(command.Y, true, token);
    }

#pragma warning disable CA2252
    public static async ValueTask<SubtractCommand> ReadFromAsync<TReader>(TReader reader, CancellationToken token)
#pragma warning restore CA2252
        where TReader : notnull, IAsyncBinaryReader
    {
        return new SubtractCommand
        {
            X = await reader.ReadInt32Async(true, token),
            Y = await reader.ReadInt32Async(true, token)
        };
    }
}


public struct LogSnapshotCommand : ISerializable<LogSnapshotCommand>
{
    public const int Id = 1;

    public string X { get; set; }

    long? IDataTransferObject.Length => sizeof(int);

    public async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token)
        where TWriter : notnull, IAsyncBinaryWriter
    {
        var command = this;
        Console.WriteLine("1 Writing snapshot WriteToAsync");
        var asMemory = command.X.AsMemory();
        await writer.WriteStringAsync(asMemory, new DotNext.Text.EncodingContext(Encoding.UTF8, false), LengthFormat.Plain, token);
    }

#pragma warning disable CA2252
    public static async ValueTask<LogSnapshotCommand> ReadFromAsync<TReader>(TReader reader, CancellationToken token)
#pragma warning restore CA2252
        where TReader : notnull, IAsyncBinaryReader
    {
        Console.WriteLine("1 Reading snapshot ReadFromAsync");
        return new LogSnapshotCommand
        {
            X = await reader.ReadStringAsync(LengthFormat.Plain, new DecodingContext(Encoding.UTF8, false), token)
        };
    }
}

public struct LogSnapshotCommand2 : ISerializable<LogSnapshotCommand2>
{
    public const int Id = 2;

    public readonly Dictionary<string, string> keysValues;
    public readonly Dictionary<string, Dictionary<string, string>> hashsets;
    public readonly Dictionary<string, List<string>> queues;

    public LogSnapshotCommand2(Dictionary<string, string> keysValues, Dictionary<string, Dictionary<string, string>> hashsets, Dictionary<string, List<string>> queues)
    {
        this.keysValues = keysValues;
        this.hashsets = hashsets;
        this.queues = queues;
    }   


    long? IDataTransferObject.Length // optional implementation, may return null
    {
        get
        {
            // compute length of the serialized data, in bytes
            long result = sizeof(int); // 4 bytes for count
            foreach (var keyValuePair in keysValues)
            {
                result +=  Encoding.UTF8.GetByteCount(keyValuePair.Key) + Encoding.UTF8.GetByteCount(keyValuePair.Value);
            }
            
            // compute length of the serialized data, in bytes
            result += sizeof(int);
            foreach (var queue in queues)
            {
                result += Encoding.UTF8.GetByteCount(queue.Key);
                result += sizeof(int); // 4 bytes for queue count
                queue.Value.ForEach(x => result += Encoding.UTF8.GetByteCount(x));
            }
            
            // compute length of the serialized data, in bytes
            result += sizeof(int);
            foreach (var hashset in hashsets)
            {
                result += Encoding.UTF8.GetByteCount(hashset.Key);
                result += sizeof(int); // 4 bytes for hashset count
                foreach (var keyValuePair in hashset.Value)
                {
                    result +=  Encoding.UTF8.GetByteCount(keyValuePair.Key) + Encoding.UTF8.GetByteCount(keyValuePair.Value);
                }
            }

            return result;
        }
    }


    public async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token)
        where TWriter : notnull, IAsyncBinaryWriter
    {
        // write the number of entries
        await writer.WriteInt32Async(keysValues.Count, true, token);
        // write the entries
        var context = new EncodingContext(Encoding.UTF8, true);
        foreach (var (key, value) in keysValues)
        {
            await writer.WriteStringAsync(key.AsMemory(), context, LengthFormat.Plain, token);
            await writer.WriteStringAsync(value.AsMemory(), context, LengthFormat.Plain, token);
        }
        
        // write the number of entries
        await writer.WriteInt32Async(queues.Count, true, token);
        // write the entries
        foreach (var queue in queues)
        {
            await writer.WriteStringAsync(queue.Key.AsMemory(), context, LengthFormat.Plain, token);
            await writer.WriteInt32Async(queue.Value.Count, true, token);
            foreach (var value in queue.Value)
            {
                await writer.WriteStringAsync(value.AsMemory(), context, LengthFormat.Plain, token);
            }
        }
        
        // write the number of entries
        await writer.WriteInt32Async(hashsets.Count, true, token);
        // write the entries
        foreach (var hashset in hashsets)
        {
            await writer.WriteStringAsync(hashset.Key.AsMemory(), context, LengthFormat.Plain, token);
            await writer.WriteInt32Async(hashset.Value.Count, true, token);
            foreach (var (key, value) in hashset.Value)
            {
                await writer.WriteStringAsync(key.AsMemory(), context, LengthFormat.Plain, token);
                await writer.WriteStringAsync(value.AsMemory(), context, LengthFormat.Plain, token);
            }
        }
        
    }

#pragma warning disable CA2252
    public static async ValueTask<LogSnapshotCommand2> ReadFromAsync<TReader>(TReader reader, CancellationToken token)
#pragma warning restore CA2252
        where TReader : notnull, IAsyncBinaryReader
    {
        var count = await reader.ReadInt32Async(true, token);
        var keysValues = new Dictionary<string, string>(count);
        // deserialize entries
        var context = new DecodingContext(Encoding.UTF8, true);
        while (count-- > 0)
        {
            var key = await reader.ReadStringAsync(LengthFormat.Plain, context, token);
            var value = await reader.ReadStringAsync(LengthFormat.Plain, context, token);
            keysValues.Add(key, value);
        }
        
        var countQueues = await reader.ReadInt32Async(true, token);
        var queues = new Dictionary<string, List<string>>(countQueues);
        // deserialize entries
        while (countQueues-- > 0)
        {
            var key = await reader.ReadStringAsync(LengthFormat.Plain, context, token);
            var countQueue = await reader.ReadInt32Async(true, token);
            var queue = new List<string>(countQueue);
            while (countQueue-- > 0)
            {
                var value = await reader.ReadStringAsync(LengthFormat.Plain, context, token);
                queue.Add(value);
            }
            queues.Add(key, queue);
        }
        
        var countHashsets = await reader.ReadInt32Async(true, token);
        var hashsets = new Dictionary<string, Dictionary<string, string>>(countHashsets);
        // deserialize entries
        while (countHashsets-- > 0)
        {
            var key = await reader.ReadStringAsync(LengthFormat.Plain, context, token);
            var countHashset = await reader.ReadInt32Async(true, token);
            var hashset = new Dictionary<string, string>(countHashset);
            while (countHashset-- > 0)
            {
                var keyHashset = await reader.ReadStringAsync(LengthFormat.Plain, context, token);
                var valueHashset = await reader.ReadStringAsync(LengthFormat.Plain, context, token);
                hashset.Add(keyHashset, valueHashset);
            }
            hashsets.Add(key, hashset);
        }
        
        Console.WriteLine("1 Reading snapshot ReadFromAsync");
        return new LogSnapshotCommand2(keysValues, hashsets, queues);
    }
}

#pragma warning disable CA2252
[Command<SubtractCommand>(SubtractCommand.Id)]
[Command<LogSnapshotCommand>(LogSnapshotCommand.Id)]
[Command<LogSnapshotCommand2>(LogSnapshotCommand2.Id)]
[Command<AddKeyValueCommand>(AddKeyValueCommand.Id)]
#pragma warning restore CA2252
public class MyInterpreter : CommandInterpreter
{
    private readonly string prefix;
    public long state;
    public IDictionary<string, string> payload = new Dictionary<string, string>();
    public IDictionary<string, List<string>> queues = new Dictionary<string, List<string>>();
    public IDictionary<string, Dictionary<string,string>> hashsets = new Dictionary<string, Dictionary<string, string>>();

    public MyInterpreter(string prefix)
    {
        this.prefix = prefix;
    }

    [CommandHandler]
    public async ValueTask SubtractAsync(SubtractCommand command, CancellationToken token)
    {
        state = state + 1;// valueCommand.Key - valueCommand.Value;
        Console.WriteLine($"{prefix}>MyInterpreter>Handling valueCommand SubtractAsync :{state}");
    }
    
    [CommandHandler]
    public async ValueTask AddKeyAsync(AddKeyValueCommand valueCommand, CancellationToken token)
    {
        if (payload.ContainsKey(valueCommand.Key))
        {
            payload.Add(valueCommand.Key, valueCommand.Value);    
        }
        else
        {
            payload[valueCommand.Key] = valueCommand.Value; 
        }
        Console.WriteLine($"{prefix}>MyInterpreter>Handling valueCommand SubtractAsync :{state}");
    }
    
    
    [CommandHandler(IsSnapshotHandler = true)]
    public async ValueTask HandleSnapshotAsync(LogSnapshotCommand2 command, CancellationToken token)
    {
        //Console.WriteLine($"{prefix}>MyInterpreter>Handling snapshot HandleSnapshotAsync" + state + " " + valueCommand.Key );
        Console.WriteLine($"{prefix}>MyInterpreter>Handling snapshot HandleSnapshotAsync" + JsonConvert.SerializeObject(command.keysValues) );
        payload = command.keysValues;
        queues = command.queues;
        hashsets = command.hashsets;
    }
    
}