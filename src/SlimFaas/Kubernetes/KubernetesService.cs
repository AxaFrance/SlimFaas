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
    int ReplicasAtStart = 1, int ReplicasMin = 0, int TimeoutSecondBeforeSetReplicasMin = 300,
    int NumberParallelRequest = 10,
    bool ReplicasStartAsSoonAsOneFunctionRetrieveARequest = false);

public record PodInformation(string Name, bool? Started, bool? Ready, string Ip, string DeploymentName);

[ExcludeFromCodeCoverage]
public class KubernetesService : IKubernetesService
{
    private const string ReplicasMin = "SlimFaas/ReplicasMin";
    private const string Function = "SlimFaas/Function";
    private const string ReplicasAtStart = "SlimFaas/ReplicasAtStart";

    private const string ReplicasStartAsSoonAsOneFunctionRetrieveARequest =
        "SlimFaas/ReplicasStartAsSoonAsOneFunctionRetrieveARequest";

    private const string TimeoutSecondBeforeSetReplicasMin = "SlimFaas/TimeoutSecondBeforeSetReplicasMin";
    private const string NumberParallelRequest = "SlimFaas/NumberParallelRequest";
    private const string SlimfaasDeploymentName = "slimfaas";
    private readonly KubernetesClientConfiguration _k8SConfig;
    private readonly ILogger<KubernetesService> _logger;

    public KubernetesService(ILogger<KubernetesService> logger, bool useKubeConfig)
    {
        _logger = logger;
        _k8SConfig = !useKubeConfig
            ? KubernetesClientConfiguration.InClusterConfig()
            : KubernetesClientConfiguration.BuildConfigFromConfigFile();
        _k8SConfig.SkipTlsVerify = true;
    }

    public async Task<ReplicaRequest?> ScaleAsync(ReplicaRequest request)
    {
        try
        {
            using k8s.Kubernetes client = new k8s.Kubernetes(_k8SConfig);
            string patchString = $"{{\"spec\": {{\"replicas\": {request.Replicas}}}}}";
            V1Patch patch = new V1Patch(patchString, V1Patch.PatchType.MergePatch);
            await client.PatchNamespacedDeploymentScaleAsync(patch, request.Deployment, request.Namespace);
        }
        catch (HttpOperationException e)
        {
            _logger.LogError(e, "Error while scaling kubernetes deployment {RequestDeployment}", request.Deployment);
            return request;
        }

        return request;
    }


    public async Task<DeploymentsInformations> ListFunctionsAsync(string kubeNamespace)
    {
        try
        {
            IList<DeploymentInformation>? deploymentInformationList = new List<DeploymentInformation>();
            using k8s.Kubernetes client = new k8s.Kubernetes(_k8SConfig);
            Task<V1DeploymentList>? deploymentListTask = client.ListNamespacedDeploymentAsync(kubeNamespace);
            Task<V1PodList>? podListTask = client.ListNamespacedPodAsync(kubeNamespace);
            Task<V1StatefulSetList>? statefulSetListTask = client.ListNamespacedStatefulSetAsync(kubeNamespace);

            await Task.WhenAll(deploymentListTask, podListTask, statefulSetListTask);
            V1DeploymentList? deploymentList = deploymentListTask.Result;
            IEnumerable<PodInformation> podList = MapPodInformations(podListTask.Result);
            V1StatefulSetList? statefulSetList = statefulSetListTask.Result;

            SlimFaasDeploymentInformation? slimFaasDeploymentInformation = statefulSetList.Items
                .Where(deploymentListItem => deploymentListItem.Metadata.Name == SlimfaasDeploymentName).Select(
                    deploymentListItem =>
                        new SlimFaasDeploymentInformation(deploymentListItem.Spec.Replicas ?? 0,
                            podList.Where(p => p.Name.StartsWith(deploymentListItem.Metadata.Name)).ToList()))
                .FirstOrDefault();

            foreach (V1Deployment? deploymentListItem in deploymentList.Items)
            {
                IDictionary<string, string>? annotations = deploymentListItem.Spec.Template.Metadata.Annotations;
                if (annotations == null || !annotations.ContainsKey(Function) ||
                    annotations[Function].ToLower() != "true")
                {
                    continue;
                }

                DeploymentInformation deploymentInformation = new DeploymentInformation(
                    deploymentListItem.Metadata.Name,
                    kubeNamespace,
                    podList.Where(p => p.DeploymentName == deploymentListItem.Metadata.Name).ToList(),
                    deploymentListItem.Spec.Replicas ?? 0,
                    annotations.ContainsKey(ReplicasAtStart)
                        ? int.Parse(annotations[ReplicasAtStart])
                        : 1, annotations.ContainsKey(ReplicasMin)
                        ? int.Parse(annotations[ReplicasMin])
                        : 1, annotations.ContainsKey(TimeoutSecondBeforeSetReplicasMin)
                        ? int.Parse(annotations[TimeoutSecondBeforeSetReplicasMin])
                        : 300, annotations.ContainsKey(NumberParallelRequest)
                        ? int.Parse(annotations[NumberParallelRequest])
                        : 10, annotations.ContainsKey(
                                  ReplicasStartAsSoonAsOneFunctionRetrieveARequest) &&
                              annotations[ReplicasStartAsSoonAsOneFunctionRetrieveARequest].ToLower() == "true");
                deploymentInformationList.Add(deploymentInformation);
            }

            return new DeploymentsInformations(deploymentInformationList,
                slimFaasDeploymentInformation ?? new SlimFaasDeploymentInformation(1, new List<PodInformation>()));
        }
        catch (HttpOperationException e)
        {
            _logger.LogError(e, "Error while listing kubernetes functions");
            return new DeploymentsInformations(new List<DeploymentInformation>(),
                new SlimFaasDeploymentInformation(1, new List<PodInformation>()));
        }
    }

    private static IEnumerable<PodInformation> MapPodInformations(V1PodList v1PodList)
    {
        foreach (V1Pod? item in v1PodList.Items)
        {
            string? podIp = item.Status.PodIP;
            if (string.IsNullOrEmpty(podIp))
            {
                continue;
            }

            V1ContainerStatus? containerStatus = item.Status.ContainerStatuses.FirstOrDefault();
            bool ready = containerStatus?.Ready ?? false;
            bool started = containerStatus?.Started ?? false;
            string? podName = item.Metadata.Name;
            string deploymentName = ExtractPodDeploymentNameFrom(item.Metadata.GenerateName);

            PodInformation podInformation = new PodInformation(podName, started, ready, podIp, deploymentName);
            yield return podInformation;
        }
    }

    public static string ExtractPodDeploymentNameFrom(string generalName)
    {
        string[] names = generalName.Split('-');
        if (names.Length <= 0)
        {
            return string.Empty;
        }

        StringBuilder realName = new StringBuilder(names[0]);
        for (int i = 1; i < names.Length - 2; i++)
        {
            realName.Append($"-{names[i]}");
        }

        return realName.ToString();
    }
}
