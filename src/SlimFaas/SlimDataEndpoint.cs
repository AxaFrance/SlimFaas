using SlimFaas.Kubernetes;

namespace SlimFaas;

public class SlimDataEndpoint
{
    public static string Get(PodInformation podInformation1)
    {
        var baseSlimDataUrl = Environment.GetEnvironmentVariable(EnvironmentVariables.BaseSlimDataUrl) ??
                EnvironmentVariables.BaseSlimDataUrlDefault;
        if (!String.IsNullOrEmpty(baseSlimDataUrl))
        {
            baseSlimDataUrl = baseSlimDataUrl.Replace("{pod_name}", podInformation1.Name);
            baseSlimDataUrl = baseSlimDataUrl.Replace("{pod_ip}", podInformation1.Ip);
        }

        return baseSlimDataUrl;
    }
}
