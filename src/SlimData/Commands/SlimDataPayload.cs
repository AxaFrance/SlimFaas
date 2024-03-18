namespace RaftNode;

public struct SlimDataPayload
{
    public IDictionary<string, string> KeyValues { get; set; }
    public IDictionary<string, List<string>> Queues { get; set; }
    
    public Dictionary<string, List<ReadOnlyMemory<byte>>> QueuesBin { get; set; }
    public IDictionary<string, Dictionary<string, string>> Hashsets { get; set; }
}