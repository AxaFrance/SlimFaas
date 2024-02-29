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
            var queues =  interpreter.queues;
            var hashsets = interpreter.hashsets;
            
            LogSnapshotCommand command = new(keysValues, hashsets, queues);
            await command.WriteToAsync(writer, token).ConfigureAwait(false);
        }
    }
}