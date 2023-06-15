using System.Diagnostics.CodeAnalysis;
using k8s;
using k8s.Autorest;
using k8s.Models;

namespace SlimFaas;

public record ReplicaRequest
{
    public string Deployment { get; set; }
    public string Namespace { get; set; }
    public int Replicas { get; set; }
}

public record SlimFaasDeploymentInformation
{
    public int? Replicas { get; set; }
}

public record DeploymentsInformations
{
    public IList<DeploymentInformation> Functions { get; set; }
    
    public SlimFaasDeploymentInformation SlimFaas { get; set; } 
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

[ExcludeFromCodeCoverage]
public class KubernetesService : IKubernetesService
{
    private readonly ILogger<KubernetesService> _logger;
    private readonly KubernetesClientConfiguration _k8SConfig;

    public KubernetesService(IConfiguration config, ILogger<KubernetesService> logger)
    {
        _logger = logger;
        var useKubeConfig = bool.Parse(config["UseKubeConfig"] ?? "false");
        _k8SConfig = !useKubeConfig ? KubernetesClientConfiguration.InClusterConfig() :
            KubernetesClientConfiguration.BuildConfigFromConfigFile();
        _k8SConfig.SkipTlsVerify = true;
    }
    
    public async Task<ReplicaRequest?> ScaleAsync(ReplicaRequest? request)
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
            _logger.LogError(e, "Error while scaling kubernetes deployment" + request.Deployment);
            return request;
        }

        return request;
    }

    private const string ReplicasMin = "SlimFaas/ReplicasMin";
    private const string Function = "SlimFaas/Function";
    private const string ReplicasAtStart = "SlimFaas/ReplicasAtStart";
    private const string ReplicasStartAsSoonAsOneFunctionRetrieveARequest = "SlimFaas/ReplicasStartAsSoonAsOneFunctionRetrieveARequest";
    private const string TimeoutSecondBeforeSetReplicasMin = "SlimFaas/TimeoutSecondBeforeSetReplicasMin";
    private const string NumberParallelRequest = "SlimFaas/NumberParallelRequest";

    public async Task<DeploymentsInformations> ListFunctionsAsync(string kubeNamespace)
    {
        try
        {
            IList<DeploymentInformation>? deploymentInformationList = new List<DeploymentInformation>();
                using var client = new Kubernetes(_k8SConfig);
                var deploymentList = await client.ListNamespacedDeploymentAsync(kubeNamespace);
                
                var slimFaasDeploymentInformation = deploymentList.Items.Where(deploymentListItem => deploymentListItem.Metadata.Name == "slimfaas").Select(deploymentListItem => new SlimFaasDeploymentInformation
                {
                    Replicas = deploymentListItem.Spec.Replicas
                }).FirstOrDefault();
                
                foreach (var deploymentListItem in deploymentList.Items)
                {
                    var annotations = deploymentListItem.Spec.Template.Metadata.Annotations;
                    if (annotations == null || !annotations.ContainsKey(Function) ||
                        annotations[Function].ToLower() != "true") continue;
                    var deploymentInformation = new DeploymentInformation
                    {
                        Deployment = deploymentListItem.Metadata.Name,
                        Namespace = kubeNamespace,
                        Replicas = deploymentListItem.Spec.Replicas,
                        ReplicasAtStart = annotations.ContainsKey(ReplicasAtStart)
                            ? int.Parse(annotations[ReplicasAtStart])
                            : 1,
                        ReplicasMin = annotations.ContainsKey(ReplicasMin)
                            ? int.Parse(annotations[ReplicasMin])
                            : 1,
                        TimeoutSecondBeforeSetReplicasMin = annotations.ContainsKey(TimeoutSecondBeforeSetReplicasMin)
                            ? int.Parse(annotations[TimeoutSecondBeforeSetReplicasMin])
                            : 300,
                        NumberParallelRequest = annotations.ContainsKey(NumberParallelRequest)
                            ? int.Parse(annotations[NumberParallelRequest])
                            : 10,
                        ReplicasStartAsSoonAsOneFunctionRetrieveARequest = annotations.ContainsKey(ReplicasStartAsSoonAsOneFunctionRetrieveARequest) &&
                                                                           annotations[ReplicasStartAsSoonAsOneFunctionRetrieveARequest].ToLower() == "true"
                    };
                    deploymentInformationList.Add(deploymentInformation);
                }

                return new DeploymentsInformations()
                {
                    Functions = deploymentInformationList,
                    SlimFaas = slimFaasDeploymentInformation ?? new SlimFaasDeploymentInformation()
                    {
                    Replicas = 1,
                }
                };

        }
        catch (HttpOperationException e)
        {
            _logger.LogError(e, "Error while listing kubernetes functions");
            return new DeploymentsInformations()
            {
                Functions = new List<DeploymentInformation>(),
                SlimFaas = new SlimFaasDeploymentInformation()
                {
                    Replicas = 1,
                }
            };

        }
    }
    
}