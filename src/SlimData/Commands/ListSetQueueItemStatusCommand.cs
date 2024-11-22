using System.Text;
using DotNext.IO;
using DotNext.Runtime.Serialization;
using DotNext.Text;

namespace SlimData.Commands;

public struct ListSetQueueItemStatusCommand  : ISerializable<ListSetQueueItemStatusCommand>
{
    public const int Id = 15;

    public string Identifier { get; set; }
    public string Key { get; set; }
    
    public int HttpCode { get; set; }
    
    public long NowTicks { get; set; }

    public async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token) where TWriter : notnull, IAsyncBinaryWriter
    {
        var command = this;
        await writer.EncodeAsync(command.Identifier.AsMemory(), new EncodingContext(Encoding.UTF8, false),
            LengthFormat.LittleEndian, token).ConfigureAwait(false);
        await writer.EncodeAsync(command.Key.AsMemory(), new EncodingContext(Encoding.UTF8, false),
            LengthFormat.LittleEndian, token).ConfigureAwait(false);
        await writer.WriteLittleEndianAsync(HttpCode, token).ConfigureAwait(false);
        await writer.WriteLittleEndianAsync(NowTicks, token).ConfigureAwait(false);
    }

    long? IDataTransferObject.Length => Encoding.UTF8.GetByteCount(Identifier)  + sizeof(int);

    public static async ValueTask<ListSetQueueItemStatusCommand> ReadFromAsync<TReader>(TReader reader, CancellationToken token) where TReader : notnull, IAsyncBinaryReader
    {
        var identifier = await reader.DecodeAsync( new DecodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token: token).ConfigureAwait(false);
        var key = await reader.DecodeAsync( new DecodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token: token).ConfigureAwait(false);
        
        return new ListSetQueueItemStatusCommand
        {
            Identifier = identifier.ToString(),
            Key = key.ToString(),
            HttpCode = await reader.ReadLittleEndianAsync<Int32>(token).ConfigureAwait(false),
            NowTicks = await reader.ReadLittleEndianAsync<Int64>(token).ConfigureAwait(false)
        };
    }
}