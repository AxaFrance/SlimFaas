using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Caching.Memory;

namespace LightFaas;


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

public class KubernetesService
{
    private readonly IMemoryCache _memoryCache;
    private KubernetesClientConfiguration k8sConfig = null;
    private IList<string> cacheKeys = new List<string>();

    public KubernetesService(IConfiguration config, IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
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

    private const string ReplicasMin = "LightFaas/ReplicasMin";
    private const string Function = "LightFaas/Function";
    private const string ReplicasAtStart = "LightFaas/ReplicasAtStart";
    private const string ReplicasStartAsSoonAsOneFunctionRetrieveARequest = "LightFaas/ReplicasStartAsSoonAsOneFunctionRetrieveARequest";
    private const string TimeoutSecondBeforeSetReplicasMin = "LightFaas/TimeoutSecondBeforeSetReplicasMin";
    private const string NumberParallelRequest = "LightFaas/NumberParallelRequest";

    public async Task<IList<DeploymentInformation>> ListFunctionsAsync(string kubeNamespace)
    {
        try
        {
            var _key = $"ListFunctionsAsync({kubeNamespace})";
            if (!cacheKeys.Contains(_key))
                cacheKeys.Add(_key);
            var cacheEntry = await _memoryCache.GetOrCreateAsync(_key, async entry =>
            {
                IList<DeploymentInformation> deploymentInformationList = new List<DeploymentInformation>();
                using var client = new Kubernetes(k8sConfig);
                var deploymentList = await client.ListNamespacedDeploymentAsync(kubeNamespace);
                foreach (var deploymentListItem in deploymentList.Items)
                {
                    var annotations = deploymentListItem.Spec.Template.Metadata.Annotations;
                    if (annotations != null && annotations.ContainsKey(Function) &&
                        annotations[Function].ToLower() == "true")
                    {
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
                }

                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5);
                entry.SlidingExpiration = TimeSpan.FromMinutes(1);
                return deploymentInformationList;
            });
            return cacheEntry;
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