using SlimFaas.Kubernetes;

namespace SlimFaas;

public class SlimDataEndpoint
{
    public static string Get(PodInformation podInformation)
    {
        string baseSlimDataUrl = Environment.GetEnvironmentVariable(EnvironmentVariables.BaseSlimDataUrl) ??
                                 EnvironmentVariables.BaseSlimDataUrlDefault;
        if (!string.IsNullOrEmpty(baseSlimDataUrl))
        {
            baseSlimDataUrl = baseSlimDataUrl.Replace("{pod_name}", podInformation.Name);
            baseSlimDataUrl = baseSlimDataUrl.Replace("{pod_ip}", podInformation.Ip);
        }

        return baseSlimDataUrl;
    }
}
