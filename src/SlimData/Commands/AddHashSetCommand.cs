using System.Text;
using DotNext.IO;
using DotNext.Runtime.Serialization;
using DotNext.Text;

namespace RaftNode;

public struct AddHashSetCommand : ISerializable<AddHashSetCommand>
{
    public const int Id = 1;

    public string Key { get; set; }
    public Dictionary<string, string> Value { get; set; }

    long? IDataTransferObject.Length // optional implementation, may return null
    {
        get
        {
            // compute length of the serialized data, in bytes
            long result = Encoding.UTF8.GetByteCount(Key); 
            result += sizeof(int); // 4 bytes for count
            foreach (var keyValuePair in Value)
            {
                result +=  Encoding.UTF8.GetByteCount(keyValuePair.Key) + Encoding.UTF8.GetByteCount(keyValuePair.Value);
            }
            return result;
        }
    }

    public async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token)
        where TWriter : notnull, IAsyncBinaryWriter
    {
        var command = this;
        await writer.WriteStringAsync(command.Key.AsMemory(), new EncodingContext(Encoding.UTF8, false), LengthFormat.Plain, token);
        // write the number of entries
        await writer.WriteInt32Async(Value.Count, true, token);
        // write the entries
        var context = new EncodingContext(Encoding.UTF8, true);
        foreach (var (key, value) in Value)
        {
            await writer.WriteStringAsync(key.AsMemory(), context, LengthFormat.Plain, token);
            await writer.WriteStringAsync(value.AsMemory(), context, LengthFormat.Plain, token);
        }
    }

#pragma warning disable CA2252
    public static async ValueTask<AddHashSetCommand> ReadFromAsync<TReader>(TReader reader, CancellationToken token)
#pragma warning restore CA2252
        where TReader : notnull, IAsyncBinaryReader
    {
        var key = await reader.ReadStringAsync(LengthFormat.Plain, new DecodingContext(Encoding.UTF8, false), token);
        
        var count = await reader.ReadInt32Async(true, token);
        var keysValues = new Dictionary<string, string>(count);
        // deserialize entries
        var context = new DecodingContext(Encoding.UTF8, true);
        while (count-- > 0)
        {
            var key_ = await reader.ReadStringAsync(LengthFormat.Plain, context, token);
            var value = await reader.ReadStringAsync(LengthFormat.Plain, context, token);
            keysValues.Add(key_, value);
        }
        
        return new AddHashSetCommand
        {
            Key = key,
            Value = keysValues
        };
    }
}