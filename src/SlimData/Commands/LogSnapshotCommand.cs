using System.Text;
using DotNext.IO;
using DotNext.Runtime.Serialization;
using DotNext.Text;

namespace RaftNode;

public readonly struct LogSnapshotCommand(Dictionary<string, string> keysValues,
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
            Console.WriteLine("Length LogSnapshotCommand");
            // compute length of the serialized data, in bytes
            long result = sizeof(Int32); // 4 bytes for count
            foreach (var keyValuePair in keysValues)
                result += Encoding.UTF8.GetByteCount(keyValuePair.Key) + Encoding.UTF8.GetByteCount(keyValuePair.Value);

            // compute length of the serialized data, in bytes
            result += sizeof(Int32);
            foreach (var queue in queues)
            {
                result += Encoding.UTF8.GetByteCount(queue.Key);
                result += sizeof(Int32); // 4 bytes for queue count
                queue.Value.ForEach(x => result += Encoding.UTF8.GetByteCount(x));
            }

            // compute length of the serialized data, in bytes
            result += sizeof(Int32);
            foreach (var hashset in hashsets)
            {
                result += Encoding.UTF8.GetByteCount(hashset.Key);
                result += sizeof(Int32); // 4 bytes for hashset count
                foreach (var keyValuePair in hashset.Value)
                    result += Encoding.UTF8.GetByteCount(keyValuePair.Key) +
                              Encoding.UTF8.GetByteCount(keyValuePair.Value);
            }
            Console.WriteLine("Length LogSnapshotCommand: " + result);
            return result;
        }
    }


    public async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token)
        where TWriter : notnull, IAsyncBinaryWriter
    {
        try
        {
            Console.WriteLine("Writing LogSnapshotCommand");
            // write the number of entries
            await writer.WriteLittleEndianAsync(keysValues.Count, token).ConfigureAwait(false);
            // write the entries
            foreach (var (key, value) in keysValues)
            {
                await writer.EncodeAsync(key.AsMemory(), new EncodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token).ConfigureAwait(false);
                await writer.EncodeAsync(value.AsMemory(), new EncodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token).ConfigureAwait(false);
            }

            // write the number of entries
            await writer.WriteLittleEndianAsync(queues.Count, token).ConfigureAwait(false);
            // write the entries
            foreach (var queue in queues)
            {
                await writer.EncodeAsync(queue.Key.AsMemory(), new EncodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token).ConfigureAwait(false);
                await writer.WriteLittleEndianAsync(queue.Value.Count, token).ConfigureAwait(false);
                foreach (var value in queue.Value)
                    await writer.EncodeAsync(value.AsMemory(), new EncodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token).ConfigureAwait(false);
            }

            // write the number of entries
            await writer.WriteLittleEndianAsync(hashsets.Count, token).ConfigureAwait(false);
            // write the entries
            foreach (var hashset in hashsets)
            {
                await writer.EncodeAsync(hashset.Key.AsMemory(), new EncodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token).ConfigureAwait(false);
                await writer.WriteLittleEndianAsync(hashset.Value.Count, token).ConfigureAwait(false);
                foreach (var (key, value) in hashset.Value)
                {
                    await writer.EncodeAsync(key.AsMemory(), new EncodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token).ConfigureAwait(false);
                    await writer.EncodeAsync(value.AsMemory(), new EncodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token).ConfigureAwait(false);
                }
            }
            Console.WriteLine("Finished Writing LogSnapshotCommand");
        
        }
        catch (Exception e)
        {
            Console.WriteLine("Error Writing LogSnapshotCommand");
            Console.WriteLine(e);
            throw;
        }
    }

#pragma warning disable CA2252
    public static async ValueTask<LogSnapshotCommand> ReadFromAsync<TReader>(TReader reader, CancellationToken token)
#pragma warning restore CA2252
        where TReader : notnull, IAsyncBinaryReader
    {
        try
        {
            Console.WriteLine("Reading LogSnapshotCommand");
            var count = await reader.ReadLittleEndianAsync<Int32>(token);
            Console.WriteLine("1");
            var keysValues = new Dictionary<string, string>(count);
            Console.WriteLine("2");
            // deserialize entries;
            Console.WriteLine("3");
            while (count-- > 0)
            {
                var key = await reader.DecodeAsync(new DecodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token: token)
                    .ConfigureAwait(false);
                var value = await reader.DecodeAsync(new DecodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token: token)
                    .ConfigureAwait(false);
                keysValues.Add(key.ToString(), value.ToString());
                Console.WriteLine("4:" + key.ToString() + " " + value.ToString());
            }

            var countQueues = await reader.ReadLittleEndianAsync<Int32>(token).ConfigureAwait(false);
            Console.WriteLine("5:" + countQueues);
            var queues = new Dictionary<string, List<string>>(countQueues);
            Console.WriteLine("6");
            // deserialize entries
            while (countQueues-- > 0)
            {
                Console.WriteLine("7");
                var key = await reader.DecodeAsync(new DecodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token: token)
                    .ConfigureAwait(false);
                Console.WriteLine("7:"+key.ToString());
                var countQueue = await reader.ReadLittleEndianAsync<Int32>(token);
                Console.WriteLine("8:"+countQueue);
                var queue = new List<string>(countQueue);
                while (countQueue-- > 0)
                {
                    var value = await reader.DecodeAsync(new DecodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token: token)
                        .ConfigureAwait(false);
                    queue.Add(value.ToString());
                    Console.WriteLine("9");
                }

                queues.Add(key.ToString(), queue);
                Console.WriteLine("10");
            }

            var countHashsets = await reader.ReadLittleEndianAsync<Int32>(token).ConfigureAwait(false);
            var hashsets = new Dictionary<string, Dictionary<string, string>>(countHashsets);
            Console.WriteLine("11");
            // deserialize entries
            while (countHashsets-- > 0)
            {
                var key = await reader.DecodeAsync(new DecodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token: token)
                    .ConfigureAwait(false);
                Console.WriteLine("12");
                var countHashset = await reader.ReadLittleEndianAsync<Int32>(token).ConfigureAwait(false);
                Console.WriteLine("13");
                var hashset = new Dictionary<string, string>(countHashset);
                while (countHashset-- > 0)
                {
                    var keyHashset = await reader.DecodeAsync(new DecodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token: token)
                        .ConfigureAwait(false);
                    Console.WriteLine("14");
                    var valueHashset = await reader.DecodeAsync(new DecodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token: token)
                        .ConfigureAwait(false);
                    Console.WriteLine("15");
                    hashset.Add(keyHashset.ToString(), valueHashset.ToString());
                }

                hashsets.Add(key.ToString(), hashset);
                Console.WriteLine("16");
            }
            Console.WriteLine("Finished Reading LogSnapshotCommand");
            return new LogSnapshotCommand(keysValues, hashsets, queues);
        }
        catch (Exception e)
        {
            Console.WriteLine("Error Reading LogSnapshotCommand");
            Console.WriteLine(e);
            throw;
        }
    }
}