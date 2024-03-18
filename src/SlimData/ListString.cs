using System.Text.Json.Serialization;
using MemoryPack;

namespace SlimData;

[MemoryPackable]
public partial class ListString 
{
    public List<string> Items { get; set; }
}

[JsonSerializable(typeof(ListString))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class ListStringSerializerContext : JsonSerializerContext
{
}