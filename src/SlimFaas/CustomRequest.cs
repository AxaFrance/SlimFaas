using System.Text.Json.Serialization;

namespace SlimFaas;

public record struct CustomRequest(IList<CustomHeader> Headers, IList<CustomForm> Form, IList<CustomFormFile> FormFiles, string FunctionName, string Path, string Method)
{
    public string Query { get; set; }
    public string Body { get; set; }
    public string ContentType { get; set; }
}

public record struct CustomHeader(string Key, string?[] Values);

public record struct CustomForm(string Key, string?[] Values);

public record struct CustomFormFile(string Key, byte[] Value, string Filename);

[JsonSerializable(typeof(CustomRequest))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class CustomRequestSerializerContext : JsonSerializerContext
{
    
}