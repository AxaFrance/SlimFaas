namespace RaftNode;

public struct SupplierPayload
{
    public IDictionary<string, string> KeyValues { get; set; }
    public IDictionary<string, List<string>> Queues { get; set; }
    public IDictionary<string, Dictionary<string,string>> Hashsets { get; set; }
}
