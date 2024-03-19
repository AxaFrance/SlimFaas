using System.Text.Json.Serialization;
using MemoryPack;

namespace SlimData;

[MemoryPackable]
public partial class ListString 
{
    public List<byte[]> Items { get; set; }
}