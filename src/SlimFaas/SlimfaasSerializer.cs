using System.Text.Json;

namespace SlimFaas;

public class SlimfaasSerializer
{
    public static byte[] GetBytes(string str)
    {
        byte[] bytes = new byte[str.Length * sizeof(char)];
        Buffer.BlockCopy(str.ToCharArray(), 0, bytes, 0, bytes.Length);
        return bytes;
    }

    // Do NOT use on arbitrary bytes; only use on GetBytes's output on the SAME system
    public static string GetString(byte[] bytes)
    {
        char[] chars = new char[bytes.Length / sizeof(char)];
        Buffer.BlockCopy(bytes, 0, chars, 0, bytes.Length);
        return new string(chars);
    }

    public static CustomRequest  Deserialize(string data)
    {
        return JsonSerializer.Deserialize(data, CustomRequestSerializerContext.Default.CustomRequest);
    }

    public static string Serialize(CustomRequest data)
    {
        return JsonSerializer.Serialize(data, CustomRequestSerializerContext.Default.CustomRequest);
    }

}
