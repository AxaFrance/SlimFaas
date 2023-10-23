using System.Text;
using DotNext.IO;
using DotNext.Runtime.Serialization;
using DotNext.Text;

namespace RaftNode;

public struct ListRightPopCommand: ISerializable<ListRightPopCommand>
{
    public const int Id = 4;

    public string Key { get; set; }

    long? IDataTransferObject.Length => sizeof(int);

    public async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token)
        where TWriter : notnull, IAsyncBinaryWriter
    {
        var command = this;
        await writer.WriteStringAsync(command.Key.AsMemory(), new EncodingContext(Encoding.UTF8, false), LengthFormat.Plain, token);
    }

#pragma warning disable CA2252
    public static async ValueTask<ListRightPopCommand> ReadFromAsync<TReader>(TReader reader, CancellationToken token)
#pragma warning restore CA2252
        where TReader : notnull, IAsyncBinaryReader
    {
        return new ListRightPopCommand
        {
            Key = await reader.ReadStringAsync(LengthFormat.Plain, new DecodingContext(Encoding.UTF8, false), token),
        };
    }
}