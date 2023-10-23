using System.Collections.Concurrent;
using System.Text;
using DotNext;
using DotNext.IO;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Text;

namespace RaftNode;



internal sealed class SimplePersistentState : MemoryBasedStateMachine, ISupplier<SupplierPayload>
{
    internal const string LogLocation = "logLocation";

    public SlimDataInterpreter interpreter = new SlimDataInterpreter("SimplePersistentState");
    private sealed class SimpleSnapshotBuilder : IncrementalSnapshotBuilder
    {
        //private SupplierPayload value;
        //private ConcurrentQueue<string> values;
        

        public SimpleSnapshotBuilder(in SnapshotBuilderContext context)
            : base(context)
        {
        }
        public SlimDataInterpreter interpreter = new("SimpleSnapshotBuilder");


        protected override async ValueTask ApplyAsync(LogEntry entry)
        {
            if (entry.IsSnapshot)
            {
                Console.WriteLine("SimpleSnapshotBuilder>entry.IsSnapshot ----------------------------");
            }
            Console.WriteLine("SimpleSnapshotBuilder>Building snapshot ApplyAsync");
            await interpreter.InterpretAsync(entry);
           //string value = await entry.ToStringAsync(Encoding.UTF8);
           //Console.WriteLine(value);
           /* switch (await entry.DeserializeFromJsonAsync())
           {
               case SubtractCommand command:
                   Console.WriteLine("Building snapshot SubtractCommand ------------");
                   break;
               case LogSnapshotCommand command:
                   Console.WriteLine("Building snapshot LogSnapshotCommand ------------ " + command.Key);
                   break;
           }*/
           
           //values.Enqueue((string) value);
           //values.Add(entry);
           //value = JsonConvert.SerializeObject((SupplierPayload) await entry.DeserializeFromJsonAsync());
        }

        public override async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token)
        {
            Console.WriteLine($"SimpleSnapshotBuilder>Building snapshot WriteToAsync");
            /*foreach (var entry in values)
            {
                Console.WriteLine(entry);
              // var value = JsonConvert.SerializeObject((SupplierPayload) await entry.DeserializeFromJsonAsync());
            }*/

           //var log = interpreter.CreateLogEntry( new LogSnapshotCommand() { Key =  }, 1);
           //await log.WriteToAsync(writer, token);
           //log.WriteToAsync()

           //var asMemory = interpreter.state.ToString().AsMemory();
           //await writer.WriteStringAsync(asMemory, new DotNext.Text.EncodingContext(Encoding.UTF8, false), LengthFormat.Plain, token);
           // write the number of entries
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
                   await writer.WriteStringAsync(key.AsMemory(), context, LengthFormat.Plain, token);
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
        try
        {
            if (entry.IsSnapshot)
            {
                Console.WriteLine("SimplePersistentState>entry.IsSnapshot ----------------------------");
            }
            Console.WriteLine("SimplePersistentState>UpdateValue");
            await interpreter.InterpretAsync(entry);
            
            /*
            SupplierPayload content;
            if (entry.IsSnapshot)
            {
                var jsonStr = await entry.GetReader().ReadStringAsync(LengthFormat.Plain, new DotNext.Text.DecodingContext(Encoding.UTF8, false));
                if (String.IsNullOrEmpty(jsonStr)) return;
                content = JsonConvert.DeserializeObject<SupplierPayload>(jsonStr);
                Console.WriteLine("Received snapshot {0} from the leader node ont {1}", content.Key, entries.Count);
            }
            else
            {
                switch (await entry.DeserializeFromJsonAsync())
                {
                    case SupplierPayload command:
                        entries.Add(command);
                        Console.WriteLine("Received value {0} from the leader node on {1}", command.Key, entries.Count);
                        //Value = command.Key - command.Value; // interpreting the command
                        break;
                }
                
                
                //content = (SupplierPayload) await entry.DeserializeFromJsonAsync();
              
            }*/
        
        //    entries.Add(content); 
        } catch (Exception e)
        {
            Console.WriteLine("SimplePersistentState>Cela foire ici mon coco");
            Console.WriteLine(e);
            throw;
        }
    }

    protected override ValueTask ApplyAsync(LogEntry entry)
        => entry.Length == 0L ? new ValueTask() : UpdateValue(entry);
    
    

    protected override SnapshotBuilder CreateSnapshotBuilder(in SnapshotBuilderContext context)
    {
      //  return null;
        Console.WriteLine("SimplePersistentState>Building snapshot");
        return new SimpleSnapshotBuilder(context);
    }
    
}