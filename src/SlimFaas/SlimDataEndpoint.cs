using SlimFaas.Kubernetes;

namespace SlimFaas;

public class SlimDataEndpoint
{
    public static string Get(PodInformation podInformation)
    {
        var baseSlimDataUrl = Environment.GetEnvironmentVariable(EnvironmentVariables.BaseSlimDataUrl) ??
                EnvironmentVariables.BaseSlimDataUrlDefault;
        if (!String.IsNullOrEmpty(baseSlimDataUrl))
        {
            baseSlimDataUrl = baseSlimDataUrl.Replace("{pod_name}", podInformation.Name);
            baseSlimDataUrl = baseSlimDataUrl.Replace("{pod_ip}", podInformation.Ip);
        }

        return baseSlimDataUrl;
    }
}
