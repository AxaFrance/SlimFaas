using System.Text.Json.Serialization;
using MemoryPack;

namespace SlimData;

public record QueueData(string Id, byte[] Data);

[MemoryPackable]
public partial class ListString 
{
    public List<QueueData> Items { get; set; }
}