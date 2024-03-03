using System.Text;
using DotNext;
using DotNext.IO;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.Commands;
using DotNext.Text;

namespace RaftNode;

public sealed class SlimPersistentState : MemoryBasedStateMachine, ISupplier<SupplierPayload>
{
    public const string LogLocation = "logLocation";

    private readonly SlimDataState _state = new(new Dictionary<string, Dictionary<string, string>>(), new Dictionary<string, string>(), new Dictionary<string, List<string>>());
    public CommandInterpreter Interpreter { get; }

    public SlimPersistentState(string path)
        : base(path, 50, new Options { InitialPartitionSize = 50 * 8, UseCaching = true })
    {
        Interpreter = SlimDataInterpreter.InitInterpreter(_state);
    }

    public SlimPersistentState(IConfiguration configuration)
        : this(configuration[LogLocation])
    {
    }

    SupplierPayload ISupplier<SupplierPayload>.Invoke()
    {
        return new SupplierPayload()
        {
            KeyValues = _state.keyValues,
            Queues = _state.queues,
            Hashsets = _state.hashsets
        };
    }

    private async ValueTask UpdateValue(LogEntry entry)
    {
        await Interpreter.InterpretAsync(entry);
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
        private readonly SlimDataState _state = new(new Dictionary<string, Dictionary<string, string>>(), new Dictionary<string, string>(), new Dictionary<string, List<string>>());   
        private readonly CommandInterpreter _interpreter;

        public SimpleSnapshotBuilder(in SnapshotBuilderContext context)
            : base(context)
        {
            _interpreter = SlimDataInterpreter.InitInterpreter(_state);
        }


        protected override async ValueTask ApplyAsync(LogEntry entry)
        {
            await _interpreter.InterpretAsync(entry);
        }

        public override async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token)
        {
            var keysValues = _state.keyValues;
            var queues =  _state.queues;
            var hashsets = _state.hashsets;
            
            LogSnapshotCommand command = new(keysValues, hashsets, queues);
            await command.WriteToAsync(writer, token).ConfigureAwait(false);
        }
    }
}