using System.Text.Json.Serialization;

namespace SlimData;

public class ListString : List<string>
{
}

[JsonSerializable(typeof(ListString))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class ListStringSerializerContext : JsonSerializerContext
{
}