using SlimFaas.Kubernetes;

namespace SlimFaas;

public class SlimDataEndpoint
{
    public static string Get(PodInformation podInformation, string? baseUrl = null)
    {
        string baseSlimDataUrl = baseUrl ?? Environment.GetEnvironmentVariable(EnvironmentVariables.BaseSlimDataUrl) ??
                                 EnvironmentVariables.BaseSlimDataUrlDefault;
        string namespaceSlimFaas = Environment.GetEnvironmentVariable(EnvironmentVariables.Namespace) ?? EnvironmentVariables.NamespaceDefault;
        if (!string.IsNullOrEmpty(baseSlimDataUrl))
        {
            baseSlimDataUrl = baseSlimDataUrl.Replace("{pod_name}", podInformation.Name);
            baseSlimDataUrl = baseSlimDataUrl.Replace("{pod_ip}", podInformation.Ip);
            baseSlimDataUrl = baseSlimDataUrl.Replace("{namespace}", namespaceSlimFaas);
            baseSlimDataUrl = baseSlimDataUrl.Replace("{function_name}", podInformation.DeploymentName);
        }

        return baseSlimDataUrl;
    }
}
