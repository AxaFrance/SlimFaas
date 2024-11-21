using System.Text;
using DotNext.IO;
using DotNext.Runtime.Serialization;
using DotNext.Text;

namespace SlimData.Commands;

public struct ListLeftPushCommand : ISerializable<ListLeftPushCommand>
{
    public const int Id = 13;

    public string Key { get; set; }
    
    public string Identifier { get; set; }
    
    public ReadOnlyMemory<byte> Value { get; set; }

    long? IDataTransferObject.Length => Encoding.UTF8.GetByteCount(Key)  + Value.Length;

    public async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token)
        where TWriter : notnull, IAsyncBinaryWriter
    {
        var command = this;
        await writer.EncodeAsync(command.Key.AsMemory(), new EncodingContext(Encoding.UTF8, false),
            LengthFormat.LittleEndian, token).ConfigureAwait(false);
        await writer.EncodeAsync(command.Identifier.AsMemory(), new EncodingContext(Encoding.UTF8, false),
            LengthFormat.LittleEndian, token).ConfigureAwait(false);
        await writer.WriteAsync(command.Value, LengthFormat.Compressed, token).ConfigureAwait(false);
    }

#pragma warning disable CA2252
    public static async ValueTask<ListLeftPushCommand> ReadFromAsync<TReader>(TReader reader, CancellationToken token)
#pragma warning restore CA2252
        where TReader : notnull, IAsyncBinaryReader
    {
        var key = await reader.DecodeAsync(new DecodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token: token).ConfigureAwait(false);
        var identifier = await reader.DecodeAsync(new DecodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token: token).ConfigureAwait(false);
        using var value = await reader.ReadAsync(LengthFormat.Compressed, token: token).ConfigureAwait(false);
        return new ListLeftPushCommand
        {
            Key = key.ToString(),
            Identifier = identifier.ToString(),
            Value = value.Memory.ToArray()
        };
    }
}