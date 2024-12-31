using System.Text;
using DotNext.IO;
using DotNext.Runtime.Serialization;
using DotNext.Text;

namespace SlimData.Commands;

public struct ListRightPopCommand : ISerializable<ListRightPopCommand>
{
    public const int Id = 4;

    public string Key { get; set; }
    public int Count { get; set; }
    public long NowTicks { get; set; }

    long? IDataTransferObject.Length => Encoding.UTF8.GetByteCount(Key) + sizeof(int) + sizeof(long);

    public async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token)
        where TWriter : notnull, IAsyncBinaryWriter
    {
        var command = this;
        await writer.EncodeAsync(command.Key.AsMemory(), new EncodingContext(Encoding.UTF8, false),
            LengthFormat.LittleEndian, token).ConfigureAwait(false);
        await writer.WriteLittleEndianAsync(Count, token).ConfigureAwait(false);
        await writer.WriteLittleEndianAsync(NowTicks, token).ConfigureAwait(false);
    }

#pragma warning disable CA2252
    public static async ValueTask<ListRightPopCommand> ReadFromAsync<TReader>(TReader reader, CancellationToken token)
#pragma warning restore CA2252
        where TReader : notnull, IAsyncBinaryReader
    {
        var key = await reader.DecodeAsync( new DecodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token: token).ConfigureAwait(false);
        return new ListRightPopCommand
        {
            Key = key.ToString(),
            Count = await reader.ReadLittleEndianAsync<Int32>(token).ConfigureAwait(false),
            NowTicks = await reader.ReadLittleEndianAsync<Int64>(token).ConfigureAwait(false)
        };
    }
}