using System.Text.Json.Serialization;
using MemoryPack;

namespace SlimData;

[MemoryPackable]
public partial class ListString 
{
    public IDictionary<string, byte[]> Items { get; set; }
}