using k8s;
using k8s.Autorest;
using k8s.Models;

namespace WebApplication1;


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
        // Reading configuration to know if running inside a cluster or in local mode.
        var useKubeConfig = bool.Parse(config["UseKubeConfig"]);
        // Running inside a k8s cluser
        k8sConfig = !useKubeConfig ? KubernetesClientConfiguration.InClusterConfig() :
            // Running on dev machine
            KubernetesClientConfiguration.BuildConfigFromConfigFile();
        k8sConfig.SkipTlsVerify = true;
    }
    
    public void Scale(ReplicaRequest request)
    {
        try
        {
            // Use the config object to create a client.
            using var client = new Kubernetes(k8sConfig);
            // Set the new number of replicas
            var patchString = "{\"spec\": {\"replicas\": " + request.Replicas + "}}";
            var patch = new V1Patch(patchString, V1Patch.PatchType.MergePatch);

            // Patch the "minions" Deployment in the "default" namespace
            client.PatchNamespacedDeploymentScale(patch, request.Deployment, request.Namespace);
        }
        catch (HttpOperationException e)
        {
            Console.WriteLine(e);
            Console.WriteLine(e.Response.ReasonPhrase);
            Console.WriteLine(e.Response.Content);
        }
    }
}