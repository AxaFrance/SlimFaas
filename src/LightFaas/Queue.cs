namespace LightFaas;

public class QueueKey
{
    public string Key { get; set; }
    public int NumberParallel { get; set; }
}
class Message
{
    public string Key { get; set; }
    public string? Data { get; set; }
}
public class Queue : IQueue
{
    private readonly IList<QueueKey> _keys = new List<QueueKey>();
    
    public IList<QueueKey> Keys
    {
        get
        {
            return _keys;
        }
    }

    private readonly IList<Message> messages = new List<Message>();

    public void EnqueueAsync(string key, string data)
    {
        if(_keys.Count(k => k.Key == key) <= 0)
        {
            lock (this)
            {
                _keys.Add(new QueueKey()
                {
                    Key = key,
                    NumberParallel = 10,
                });
            }
        }

        lock (this)
        {
            messages.Add(new Message()
            {
                Data = data,
                Key = key,
            });
        }
    }
        
    public string? DequeueAsync(string key)
    {
        var data = messages.FirstOrDefault(m => m.Key == key);
        if (data == null) return null;
        lock (this)
        {
            messages.Remove(data);
        }

        return data.Data;
    }

}