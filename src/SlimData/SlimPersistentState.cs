using System.Text;
using DotNext;
using DotNext.IO;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Text;

namespace RaftNode;

public sealed class SlimPersistentState : MemoryBasedStateMachine, ISupplier<SupplierPayload>
{
    public const string LogLocation = "logLocation";

    public SlimDataInterpreter interpreter = new();


    public SlimPersistentState(string path)
        : base(path, 50, new Options { InitialPartitionSize = 50 * 8, UseCaching = true })
    {
    }

    public SlimPersistentState(IConfiguration configuration)
        : this(configuration[LogLocation])
    {
    }

    SupplierPayload ISupplier<SupplierPayload>.Invoke()
    {
        return new SupplierPayload()
        {
            KeyValues = interpreter.keyValues,
            Queues = interpreter.queues,
            Hashsets = interpreter.hashsets
        };
    }

    private async ValueTask UpdateValue(LogEntry entry)
    {
        await interpreter.InterpretAsync(entry);
    }

    protected override ValueTask ApplyAsync(LogEntry entry)
    {
        return entry.Length == 0L ? new ValueTask() : UpdateValue(entry);
    }

    protected override SnapshotBuilder CreateSnapshotBuilder(in SnapshotBuilderContext context)
    {
        return new SimpleSnapshotBuilder(context);
    }

    private sealed class SimpleSnapshotBuilder : IncrementalSnapshotBuilder
    {
        public readonly SlimDataInterpreter interpreter = new();

        public SimpleSnapshotBuilder(in SnapshotBuilderContext context)
            : base(context)
        {
        }


        protected override async ValueTask ApplyAsync(LogEntry entry)
        {
            await interpreter.InterpretAsync(entry);
        }

        public override async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token)
        {
            var keysValues = interpreter.keyValues;
            var queues = interpreter.queues;
            var hashsets = interpreter.hashsets;

            Console.WriteLine("Writing LogSnapshotCommand in SimpleSnapshotBuilder");
            await writer.WriteLittleEndianAsync(keysValues.Count, token).ConfigureAwait(false);
            // write the entries
            foreach (var (key, value) in keysValues)
            {
                await writer.EncodeAsync(key.AsMemory(), new EncodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token).ConfigureAwait(false);
                await writer.EncodeAsync(value.AsMemory(), new EncodingContext(Encoding.UTF8, false), LengthFormat.LittleEndian, token).ConfigureAwait(false);
            }

            // write the number of entries
            await writer.WriteLittleEndianAsync(keysValues.Count, token).ConfigureAwait(false);
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
            Console.WriteLine("End Wrinting LogSnapshotCommand in SimpleSnapshotBuilder");
        }
    }
}