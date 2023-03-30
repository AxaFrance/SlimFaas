using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Caching.Memory;

namespace SlimFaas;

public record ReplicaRequest
{
    public string Deployment { get; set; }
    public string Namespace { get; set; }
    public int Replicas { get; set; }
}

public record DeploymentInformation
{
    public string Deployment { get; set; }
    public string Namespace { get; set; }
    public int? Replicas { get; set; }
    public int ReplicasMin { get; set; }
    public int ReplicasAtStart { get; set; }
    public bool ReplicasStartAsSoonAsOneFunctionRetrieveARequest { get; set; }
    public int TimeoutSecondBeforeSetReplicasMin { get; set; }
    public int NumberParallelRequest { get; set; }
}

public class KubernetesService : IKubernetesService
{
    private readonly KubernetesClientConfiguration _k8SConfig;
    private readonly IList<string> _cacheKeys = new List<string>();

    public KubernetesService(IConfiguration config)
    {
        var useKubeConfig = bool.Parse(config["UseKubeConfig"]);
        _k8SConfig = !useKubeConfig ? KubernetesClientConfiguration.InClusterConfig() :
            KubernetesClientConfiguration.BuildConfigFromConfigFile();
        _k8SConfig.SkipTlsVerify = true;
    }
    
    public async Task<ReplicaRequest> ScaleAsync(ReplicaRequest request)
    {
        try
        {
            using var client = new Kubernetes(_k8SConfig);
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

        return request;
    }

    private const string ReplicasMin = "SlimFaas/ReplicasMin";
    private const string Function = "SlimFaas/Function";
    private const string ReplicasAtStart = "SlimFaas/ReplicasAtStart";
    private const string ReplicasStartAsSoonAsOneFunctionRetrieveARequest = "SlimFaas/ReplicasStartAsSoonAsOneFunctionRetrieveARequest";
    private const string TimeoutSecondBeforeSetReplicasMin = "SlimFaas/TimeoutSecondBeforeSetReplicasMin";
    private const string NumberParallelRequest = "SlimFaas/NumberParallelRequest";

    public async Task<IList<DeploymentInformation>> ListFunctionsAsync(string kubeNamespace)
    {
        try
        {
            IList<DeploymentInformation> deploymentInformationList = new List<DeploymentInformation>();
                using var client = new Kubernetes(_k8SConfig);
                var deploymentList = await client.ListNamespacedDeploymentAsync(kubeNamespace);
                foreach (var deploymentListItem in deploymentList.Items)
                {
                    var annotations = deploymentListItem.Spec.Template.Metadata.Annotations;
                    if (annotations == null || !annotations.ContainsKey(Function) ||
                        annotations[Function].ToLower() != "true") continue;
                    var deploymentInformation = new DeploymentInformation();
                    deploymentInformation.Deployment = deploymentListItem.Metadata.Name;
                    deploymentInformation.Namespace = kubeNamespace;
                    deploymentInformation.Replicas = deploymentListItem.Spec.Replicas;
                    deploymentInformation.ReplicasAtStart = annotations.ContainsKey(ReplicasAtStart)
                        ? int.Parse(annotations[ReplicasAtStart])
                        : 1;
                    deploymentInformation.ReplicasMin = annotations.ContainsKey(ReplicasMin)
                        ? int.Parse(annotations[ReplicasMin])
                        : 1;
                    deploymentInformation.TimeoutSecondBeforeSetReplicasMin =
                        annotations.ContainsKey(TimeoutSecondBeforeSetReplicasMin)
                            ? int.Parse(annotations[TimeoutSecondBeforeSetReplicasMin])
                            : 300;
                    deploymentInformation.NumberParallelRequest = annotations.ContainsKey(NumberParallelRequest)
                        ? int.Parse(annotations[NumberParallelRequest])
                        : 10;
                    deploymentInformation.ReplicasStartAsSoonAsOneFunctionRetrieveARequest =
                        annotations.ContainsKey(ReplicasStartAsSoonAsOneFunctionRetrieveARequest) &&
                        annotations[ReplicasStartAsSoonAsOneFunctionRetrieveARequest].ToLower() == "true";
                    deploymentInformationList.Add(deploymentInformation);
                }

                return deploymentInformationList;

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