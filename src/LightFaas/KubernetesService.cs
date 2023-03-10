using k8s;
using k8s.Autorest;
using k8s.Models;

namespace LightFaas;


public record ReplicaRequest
{
    public string Deployment { get; set; }
    public string Namespace { get; set; }
    public int Replicas { get; set; }
}

public class KubernetesService
{
    private KubernetesClientConfiguration k8sConfig = null;

    public KubernetesService(IConfiguration config)
    {
        var useKubeConfig = bool.Parse(config["UseKubeConfig"]);
        k8sConfig = !useKubeConfig ? KubernetesClientConfiguration.InClusterConfig() :
            KubernetesClientConfiguration.BuildConfigFromConfigFile();
        k8sConfig.SkipTlsVerify = true;
    }
    
    public async Task ScaleAsync(ReplicaRequest request)
    {
        try
        {
            using var client = new Kubernetes(k8sConfig);
            var patchString = "{\"spec\": {\"replicas\": " + request.Replicas + "}}";
            var patch = new V1Patch(patchString, V1Patch.PatchType.MergePatch);
            await client.PatchNamespacedDeploymentScaleAsync(patch, request.Deployment, request.Namespace);
        }
        catch (HttpOperationException e)
        {
            Console.WriteLine(e);
            Console.WriteLine(e.Response.ReasonPhrase);
            Console.WriteLine(e.Response.Content);
        }
    }

    public async Task<int?> GetCurrentScaleAsync(string kubeNamespace, string deploymentName)
    {
        try
        {
            using var client = new Kubernetes(k8sConfig);
            var deploymentList = await client.ListNamespacedDeploymentAsync(kubeNamespace);
            var deployment = deploymentList.Items.FirstOrDefault(i => i.Metadata.Name == deploymentName);
            return deployment?.Spec.Replicas;
        }
        catch (HttpOperationException e)
        {
            Console.WriteLine(e);
            Console.WriteLine(e.Response.ReasonPhrase);
            Console.WriteLine(e.Response.Content);
            return null;
        }
    }
}