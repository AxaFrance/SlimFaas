using System.Text.Json.Serialization;

namespace SlimFaas;

public class DictionnaryString : Dictionary<string,string>;

[JsonSerializable(typeof(DictionnaryString))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class DictionnaryStringSerializerContext : JsonSerializerContext
{
}
