namespace SlimData.Commands;

public struct SlimDataPayload
{
    public IDictionary<string, string> KeyValues { get; set; }
    
    public Dictionary<string, List<ReadOnlyMemory<byte>>> Queues { get; set; }
    public IDictionary<string, Dictionary<string, string>> Hashsets { get; set; }
}