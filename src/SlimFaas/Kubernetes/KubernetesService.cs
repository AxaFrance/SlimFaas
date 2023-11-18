using System.Diagnostics.CodeAnalysis;
using System.Text;
using k8s;
using k8s.Autorest;
using k8s.Models;

namespace SlimFaas.Kubernetes;

public record ReplicaRequest(string Deployment, string Namespace, int Replicas);

public record SlimFaasDeploymentInformation(int Replicas, IList<PodInformation> Pods);
public record DeploymentsInformations(IList<DeploymentInformation> Functions, SlimFaasDeploymentInformation SlimFaas);

public record DeploymentInformation(string Deployment, string Namespace, IList<PodInformation> Pods, int Replicas,
    int ReplicasAtStart=1, int ReplicasMin=0, int TimeoutSecondBeforeSetReplicasMin=300, int NumberParallelRequest=10,
    bool ReplicasStartAsSoonAsOneFunctionRetrieveARequest = false);

public record PodInformation(string Name, bool? Started, bool? Ready, string Ip, string DeploymentName);

[ExcludeFromCodeCoverage]
public class KubernetesService : IKubernetesService
{
    private readonly ILogger<KubernetesService> _logger;
    private readonly KubernetesClientConfiguration _k8SConfig;

    public KubernetesService(ILogger<KubernetesService> logger, bool useKubeConfig)
    {
        _logger = logger;
        _k8SConfig = !useKubeConfig ? KubernetesClientConfiguration.InClusterConfig() :
            KubernetesClientConfiguration.BuildConfigFromConfigFile();
        _k8SConfig.SkipTlsVerify = true;
    }

    public async Task<ReplicaRequest?> ScaleAsync(ReplicaRequest request)
    {
        try
        {
            using var client = new k8s.Kubernetes(_k8SConfig);
            var patchString = $"{{\"spec\": {{\"replicas\": {request.Replicas}}}}}";
            var patch = new V1Patch(patchString, V1Patch.PatchType.MergePatch);
            await client.PatchNamespacedDeploymentScaleAsync(patch, request.Deployment, request.Namespace);
        }
        catch (HttpOperationException e)
        {
            _logger.LogError(e, "Error while scaling kubernetes deployment {RequestDeployment}", request.Deployment);
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
    private const string SlimfaasDeploymentName = "slimfaas";


    public async Task<DeploymentsInformations> ListFunctionsAsync(string kubeNamespace)
    {
        try
        {
            IList<DeploymentInformation>? deploymentInformationList = new List<DeploymentInformation>();
                using var client = new k8s.Kubernetes(_k8SConfig);
                var deploymentListTask = client.ListNamespacedDeploymentAsync(kubeNamespace);
                var podListTask = client.ListNamespacedPodAsync(kubeNamespace);
                var statefulSetListTask = client.ListNamespacedStatefulSetAsync(kubeNamespace);

                await Task.WhenAll(deploymentListTask, podListTask, statefulSetListTask);
                var deploymentList = deploymentListTask.Result;
                var podList = MapPodInformations(podListTask.Result);

                var slimFaasDeploymentInformation = deploymentList.Items.Where(deploymentListItem => deploymentListItem.Metadata.Name == SlimfaasDeploymentName).Select(deploymentListItem =>
                    new SlimFaasDeploymentInformation(deploymentListItem.Spec.Replicas ?? 0, podList.Where(p => p.DeploymentName == deploymentListItem.Metadata.Name).ToList())).FirstOrDefault();

                if (slimFaasDeploymentInformation is { Pods.Count: 0 })
                {
                    var statefulSetList = statefulSetListTask.Result;
                    slimFaasDeploymentInformation = statefulSetList.Items.Where(statefulSetListItem => statefulSetListItem.Metadata.Name == SlimfaasDeploymentName).Select(statefulSetListItem =>
                        new SlimFaasDeploymentInformation(statefulSetListItem.Spec.Replicas ?? 0, podList.Where(p => p.DeploymentName == statefulSetListItem.Metadata.Name).ToList())).FirstOrDefault();
                }

                foreach (var deploymentListItem in deploymentList.Items)
                {
                    var annotations = deploymentListItem.Spec.Template.Metadata.Annotations;
                    if (annotations == null || !annotations.ContainsKey(Function) ||
                        annotations[Function].ToLower() != "true") continue;
                    var deploymentInformation = new DeploymentInformation(Deployment: deploymentListItem.Metadata.Name,
                        Namespace: kubeNamespace,
                        Pods: podList.Where(p => p.DeploymentName == deploymentListItem.Metadata.Name).ToList(),
                        Replicas: deploymentListItem.Spec.Replicas ?? 0,
                        ReplicasAtStart: annotations.ContainsKey(ReplicasAtStart)
                            ? int.Parse(annotations[ReplicasAtStart])
                            : 1, ReplicasMin: annotations.ContainsKey(ReplicasMin)
                            ? int.Parse(annotations[ReplicasMin])
                            : 1, TimeoutSecondBeforeSetReplicasMin: annotations.ContainsKey(TimeoutSecondBeforeSetReplicasMin)
                            ? int.Parse(annotations[TimeoutSecondBeforeSetReplicasMin])
                            : 300, NumberParallelRequest: annotations.ContainsKey(NumberParallelRequest)
                            ? int.Parse(annotations[NumberParallelRequest])
                            : 10, ReplicasStartAsSoonAsOneFunctionRetrieveARequest: annotations.ContainsKey(
                            ReplicasStartAsSoonAsOneFunctionRetrieveARequest) &&
                        annotations[ReplicasStartAsSoonAsOneFunctionRetrieveARequest].ToLower() == "true");
                    deploymentInformationList.Add(deploymentInformation);
                }

                return new DeploymentsInformations(Functions: deploymentInformationList,
                    SlimFaas: slimFaasDeploymentInformation ?? new SlimFaasDeploymentInformation(Replicas: 1, new List<PodInformation>()));

        }
        catch (HttpOperationException e)
        {
            _logger.LogError(e, "Error while listing kubernetes functions");
            return new DeploymentsInformations(Functions: new List<DeploymentInformation>(),
                SlimFaas: new SlimFaasDeploymentInformation(Replicas: 1, new List<PodInformation>()));
        }
    }

    private static IEnumerable<PodInformation> MapPodInformations(V1PodList v1PodList)
    {
        foreach (var item in v1PodList.Items)
        {
            var containerStatus = item.Status.ContainerStatuses.FirstOrDefault();
            if (containerStatus == null)
            {
                continue;
            }

            var ready = containerStatus.Ready;
            var started = containerStatus.Started;
            var podIp = item.Status.PodIP;
            var podName = item.Metadata.Name;
            var deploymentName = ExtractPodDeploymentNameFrom(item.Metadata.GenerateName);
            var podInformation = new PodInformation(podName, started, ready, podIp, deploymentName);

            yield return podInformation;
        }
    }

    public static string ExtractPodDeploymentNameFrom(string generalName)
    {
        var names = generalName.Split("-");
        if (names.Length <= 0)
        {
            return string.Empty;
        }
        var realName = new StringBuilder(names[0]);
        for (int i = 1; i < names.Length-2; i++)
        {
            realName.Append($"-{names[i]}");
        }
        return realName.ToString();
    }

}
