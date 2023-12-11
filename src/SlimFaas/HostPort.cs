namespace SlimFaas;

public class HostPort
{
    public static bool IsSamePort(int? requestPort, int[] ports)
    {
        if (requestPort == null)
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
