﻿using System.Text;
using DotNext.IO;
using DotNext.Runtime.Serialization;
using DotNext.Text;

namespace SlimData.Commands;

public readonly struct LogSnapshotCommand(Dictionary<string, ReadOnlyMemory<byte>> keysValues,
        Dictionary<string, Dictionary<string, string>> hashsets, Dictionary<string, List<ReadOnlyMemory<byte>>> queues)
    : ISerializable<LogSnapshotCommand>
{
    public const int Id = 5;

    public readonly Dictionary<string, ReadOnlyMemory<byte>> keysValues = keysValues;
    public readonly Dictionary<string, Dictionary<string, string>> hashsets = hashsets;
    public readonly Dictionary<string, List<ReadOnlyMemory<byte>>> queues = queues;


    long? IDataTransferObject.Length // optional implementation, may return null
    {
        get
        {
            // compute length of the serialized data, in bytes
            long result = sizeof(Int32); // 4 bytes for count
            foreach (var keyValuePair in keysValues)
                result += Encoding.UTF8.GetByteCount(keyValuePair.Key) + keyValuePair.Value.Length;

            // compute length of the serialized data, in bytes
            result += sizeof(Int32);
            foreach (var queue in queues)
            {
                result += Encoding.UTF8.GetByteCount(queue.Key);
                result += sizeof(Int32); // 4 bytes for queue count
                queue.Value.ForEach(x => result += x.Length);
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
            return result;
        }
    }


    public async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token)
        where TWriter : notnull, IAsyncBinaryWriter
    {
            // write the number of entries
            await writer.WriteLittleEndianAsync(keysValues.Count, token).ConfigureAwait(false);
            // write the entries
            foreach (var (key, value) in keysValues)
            {
                await writer.EncodeAsync(key.AsMemory(), new EncodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token).ConfigureAwait(false);
                await writer.WriteAsync(value, LengthFormat.Compressed, token).ConfigureAwait(false);
            }

            // write the number of entries
            await writer.WriteLittleEndianAsync(queues.Count, token).ConfigureAwait(false);
            // write the entries
            foreach (var queue in queues)
            {
                await writer.EncodeAsync(queue.Key.AsMemory(), new EncodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token).ConfigureAwait(false);
                await writer.WriteLittleEndianAsync(queue.Value.Count, token).ConfigureAwait(false);
                foreach (var value in queue.Value)
                    await writer.WriteAsync(value, LengthFormat.Compressed, token).ConfigureAwait(false);
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
    }

#pragma warning disable CA2252
    public static async ValueTask<LogSnapshotCommand> ReadFromAsync<TReader>(TReader reader, CancellationToken token)
#pragma warning restore CA2252
        where TReader : notnull, IAsyncBinaryReader
    {

            var count = await reader.ReadLittleEndianAsync<Int32>(token);
            var keysValues = new Dictionary<string,ReadOnlyMemory<byte>>(count);
            // deserialize entries;
            while (count-- > 0)
            {
                var key = await reader.DecodeAsync(new DecodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token: token)
                    .ConfigureAwait(false);
                using var value = await reader.ReadAsync(LengthFormat.Compressed, token: token).ConfigureAwait(false);
                keysValues.Add(key.ToString(), value.Memory);
            }

            var countQueues = await reader.ReadLittleEndianAsync<Int32>(token).ConfigureAwait(false);
            var queues = new Dictionary<string, List<ReadOnlyMemory<byte>>>(countQueues);
            // deserialize entries
            while (countQueues-- > 0)
            {
                var key = await reader.DecodeAsync(new DecodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token: token)
                    .ConfigureAwait(false);
                var countQueue = await reader.ReadLittleEndianAsync<Int32>(token);
                var queue = new List<ReadOnlyMemory<byte>>(countQueue);
                while (countQueue-- > 0)
                {
                    using var value = await reader.ReadAsync(LengthFormat.Compressed, token: token).ConfigureAwait(false);
                    queue.Add(value.Memory);
                }

                queues.Add(key.ToString(), queue);
            }

            var countHashsets = await reader.ReadLittleEndianAsync<Int32>(token).ConfigureAwait(false);
            var hashsets = new Dictionary<string, Dictionary<string, string>>(countHashsets);
            // deserialize entries
            while (countHashsets-- > 0)
            {
                var key = await reader.DecodeAsync(new DecodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token: token)
                    .ConfigureAwait(false);
                var countHashset = await reader.ReadLittleEndianAsync<Int32>(token).ConfigureAwait(false);
                var hashset = new Dictionary<string, string>(countHashset);
                while (countHashset-- > 0)
                {
                    var keyHashset = await reader.DecodeAsync(new DecodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token: token)
                        .ConfigureAwait(false);
                    var valueHashset = await reader.DecodeAsync(new DecodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token: token)
                        .ConfigureAwait(false);
                    hashset.Add(keyHashset.ToString(), valueHashset.ToString());
                }

                hashsets.Add(key.ToString(), hashset);
            }
            return new LogSnapshotCommand(keysValues, hashsets, queues);
    }
}