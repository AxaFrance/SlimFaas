using SlimFaas.Kubernetes;

namespace SlimFaas;

public class SlimDataEndpoint
{
    public static string Get(PodInformation podInformation1)
    {
        var s = Environment.GetEnvironmentVariable(EnvironmentVariables.BaseSlimDataUrl) ??
                EnvironmentVariables.BaseSlimDataUrlDefault;

        s = s.Replace("{pod_name}", podInformation1.Name);
        s = s.Replace("{pod_ip}", podInformation1.Ip);
        return s;
    }
}
