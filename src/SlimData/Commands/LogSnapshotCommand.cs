using System.Text;
using DotNext.IO;
using DotNext.Runtime.Serialization;
using DotNext.Text;

namespace RaftNode;

public struct LogSnapshotCommand(Dictionary<string, string> keysValues,
        Dictionary<string, Dictionary<string, string>> hashsets, Dictionary<string, List<string>> queues)
    : ISerializable<LogSnapshotCommand>
{
    public const int Id = 5;

    public readonly Dictionary<string, string> keysValues = keysValues;
    public readonly Dictionary<string, Dictionary<string, string>> hashsets = hashsets;
    public readonly Dictionary<string, List<string>> queues = queues;


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
    public static async ValueTask<LogSnapshotCommand> ReadFromAsync<TReader>(TReader reader, CancellationToken token)
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
        
        return new LogSnapshotCommand(keysValues, hashsets, queues);
    }
}