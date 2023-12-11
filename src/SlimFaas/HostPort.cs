namespace SlimFaas;

public class HostPort
{
    public static bool IsSamePort(int? requestPort, int[] ports)
    {
        if (requestPort != 3262)
        {
            Console.WriteLine($"requestPort: {requestPort}");
            foreach (int port in ports)
            {
                Console.WriteLine($"port: {port}");
            }
        }
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
