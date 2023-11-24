/*using System.Text.Json.Serialization;
using MemoryPack;
using MessagePack;

namespace SlimFaas;

[MemoryPackable]
public partial record struct CustomRequest(IList<CustomHeader> Headers, byte[]? Body, string FunctionName, string Path,
    string Method, string Query);

[MemoryPackable]
public partial record struct CustomHeader(string Key, string?[] Values);

*/

using System.Text.Json.Serialization;

namespace SlimFaas;

public record struct CustomRequest(IList<CustomHeader> Headers, byte[]? Body, string FunctionName, string Path,
    string Method, string Query);

public record struct CustomHeader(string Key, string?[] Values);


[JsonSerializable(typeof(CustomRequest))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class CustomRequestSerializerContext : JsonSerializerContext
{

}

