using DotNext;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.Commands;
using DotNext.Runtime.Serialization;
using SlimData.Commands;

namespace SlimData;

public sealed class SlimPersistentState : MemoryBasedStateMachine, ISupplier<SlimDataPayload>
{
    public const string LogLocation = "logLocation";

    private readonly SlimDataState _state = new(new Dictionary<string, Dictionary<string, string>>(), 
        new Dictionary<string, ReadOnlyMemory<byte>>(), 
        new Dictionary<string, List<QueueElement>>()
        );
    public CommandInterpreter Interpreter { get; }

    public  SlimPersistentState(string path)
        : base(path, 50, new Options { InitialPartitionSize = 50 * 80, UseCaching = true, UseLegacyBinaryFormat = false })
    {
        Interpreter = SlimDataInterpreter.InitInterpreter(_state);
    }

    public SlimPersistentState(IConfiguration configuration)
        : this(configuration[LogLocation])
    {
    }
    
    public LogEntry<TCommand> CreateLogEntry<TCommand>(TCommand command)
        where TCommand : struct, ISerializable<TCommand>
        => Interpreter.CreateLogEntry(command, Term);

    public SlimDataState SlimDataState
    {
        get
        {
            return _state;
        }
    }
    SlimDataPayload ISupplier<SlimDataPayload>.Invoke()
    {
        return new SlimDataPayload()
        {
            KeyValues = _state.KeyValues,
            Hashsets = _state.Hashsets,
            Queues = _state.Queues
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
        private readonly SlimDataState _state = new(new Dictionary<string, Dictionary<string, string>>(), 
            new Dictionary<string, ReadOnlyMemory<byte>>(), 
            new Dictionary<string, List<QueueElement>>()
            );   
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
            var keysValues = _state.KeyValues;
            var queues =  _state.Queues;
            var hashsets = _state.Hashsets;
            
            LogSnapshotCommand command = new(keysValues, hashsets, queues);
            await command.WriteToAsync(writer, token).ConfigureAwait(false);
        }
    }
}