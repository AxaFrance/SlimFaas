using System.Text.Json.Serialization;
using MemoryPack;

namespace SlimData;

[MemoryPackable]
public partial record QueueData(string Id, byte[] Data);

[MemoryPackable]
public partial class ListItems 
{
    public List<QueueData> Items { get; set; }
}