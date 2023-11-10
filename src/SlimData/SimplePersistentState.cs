using System.Text;
using DotNext;
using DotNext.IO;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Text;

namespace RaftNode;



public sealed class SimplePersistentState : MemoryBasedStateMachine, ISupplier<SupplierPayload>
{
    internal const string LogLocation = "logLocation";

    public SlimDataInterpreter interpreter = new("SimplePersistentState");
    private sealed class SimpleSnapshotBuilder : IncrementalSnapshotBuilder
    {

        public SimpleSnapshotBuilder(in SnapshotBuilderContext context)
            : base(context)
        {
        }
        public SlimDataInterpreter interpreter = new("SimpleSnapshotBuilder");


        protected override async ValueTask ApplyAsync(LogEntry entry)
        {
            await interpreter.InterpretAsync(entry);
        }

        public override async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token)
        {
           var keysValues = interpreter.keyValues;
           var queues = interpreter.queues;
           var hashsets = interpreter.hashsets;
           
           await writer.WriteInt32Async(keysValues.Count, true, token);
           // write the entries
           var context = new EncodingContext(Encoding.UTF8, true);
           foreach (var (key, value) in keysValues)
           {
               await writer.WriteStringAsync(key.AsMemory(), context, LengthFormat.Plain, token);
               await writer.WriteStringAsync(value.AsMemory(), context, LengthFormat.Plain, token);
           }
        
           // write the number of entries
           await writer.WriteInt32Async(queues.Count, true, token);
           // write the entries
           foreach (var queue in queues)
           {
               await writer.WriteStringAsync(queue.Key.AsMemory(), context, LengthFormat.Plain, token);
               await writer.WriteInt32Async(queue.Value.Count, true, token);
               foreach (var value in queue.Value)
               {
                   await writer.WriteStringAsync(value.AsMemory(), context, LengthFormat.Plain, token);
               }
           }
        
           // write the number of entries
           await writer.WriteInt32Async(hashsets.Count, true, token);
           // write the entries
           foreach (var hashset in hashsets)
           {
               await writer.WriteStringAsync(hashset.Key.AsMemory(), context, LengthFormat.Plain, token);
               await writer.WriteInt32Async(hashset.Value.Count, true, token);
               foreach (var (key, value) in hashset.Value)
               {
                   await writer.WriteStringAsync(key.AsMemory(), context, LengthFormat.Plain, token);
                   await writer.WriteStringAsync(value.AsMemory(), context, LengthFormat.Plain, token);
               }
           }
        }
    }
    
   
    public SimplePersistentState(string path)
        : base(path, 50, new Options { InitialPartitionSize = 50 * 8 })
    {
    }

    public SimplePersistentState(IConfiguration configuration)
        : this(configuration[LogLocation])
    {
    }

    SupplierPayload ISupplier<SupplierPayload>.Invoke() => new SupplierPayload()
    {
        KeyValues = interpreter.keyValues,
        Queues = interpreter.queues,
        Hashsets = interpreter.hashsets
    };

    private async ValueTask UpdateValue(LogEntry entry)
    {
        await interpreter.InterpretAsync(entry);
    }

    protected override ValueTask ApplyAsync(LogEntry entry)
        => entry.Length == 0L ? new ValueTask() : UpdateValue(entry);

    protected override SnapshotBuilder CreateSnapshotBuilder(in SnapshotBuilderContext context)
    {
        return new SimpleSnapshotBuilder(context);
    }
    
}