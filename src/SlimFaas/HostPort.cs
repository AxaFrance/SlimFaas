namespace SlimFaas;

public class HostPort
{
    public static bool IsSamePort(int? requestPort, int[] ports)
    {
        Console.WriteLine($"requestPort: {requestPort}");
        if (requestPort == null && ports.Contains(80))
        {
            return true;
        }

        foreach (int port in ports)
        {
            if (port == requestPort)
            {
                return true;
            }
        }

        return false;
    }
}
