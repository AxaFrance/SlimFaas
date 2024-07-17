using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using k8s;
using k8s.Autorest;
using k8s.Models;

namespace SlimFaas.Kubernetes;

public class ScheduleConfig
{
    public string TimeZoneID  { get; set; } = "GB";
    public DefaultSchedule? Default { get; set; } = new();
}

public record DefaultSchedule
{
    public List<string> WakeUp { get; init; } = new();
    public List<ScaleDownTimeout> ScaleDownTimeout { get; init; } = new();
}

public record ScaleDownTimeout
{
    public string Time { get; init; } = "00:00";
    public int Value { get; init; }
}

[JsonSerializable(typeof(ScheduleConfig))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class ScheduleConfigSerializerContext : JsonSerializerContext;


public enum FunctionVisibility
{
    Public,
    Private
}

public enum PodType
{
    Deployment,
    StatefulSet
}

public record ReplicaRequest(string Deployment, string Namespace, int Replicas, PodType PodType);

public record SlimFaasDeploymentInformation(int Replicas, IList<PodInformation> Pods);

public record DeploymentsInformations(IList<DeploymentInformation> Functions,
    SlimFaasDeploymentInformation SlimFaas, IEnumerable<PodInformation> Pods);

[JsonSerializable(typeof(DeploymentsInformations))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class DeploymentsInformationsSerializerContext : JsonSerializerContext;

public record DeploymentInformation(string Deployment, string Namespace, IList<PodInformation> Pods, int Replicas,
    int ReplicasAtStart = 1,
    int ReplicasMin = 0,
    int TimeoutSecondBeforeSetReplicasMin = 300,
    int NumberParallelRequest = 10,
    bool ReplicasStartAsSoonAsOneFunctionRetrieveARequest = false,
    PodType PodType = PodType.Deployment,
    IList<string>? DependsOn = null,
    ScheduleConfig? Schedule = null,
    IList<string>? SubscribeEvents = null,
    FunctionVisibility Visibility = FunctionVisibility.Public,
    IList<string>? PathsStartWithVisibility = null,
    IList<string>? ExcludeDeploymentsFromVisibilityPrivate = null,
    string ResourceVersion = ""
    );

public record PodInformation(string Name, bool? Started, bool? Ready, string Ip, string DeploymentName);

[ExcludeFromCodeCoverage]
public class KubernetesService : IKubernetesService
{
    private const string ReplicasMin = "SlimFaas/ReplicasMin";
    private const string Schedule = "SlimFaas/Schedule";
    private const string Function = "SlimFaas/Function";
    private const string ReplicasAtStart = "SlimFaas/ReplicasAtStart";
    private const string DependsOn = "SlimFaas/DependsOn";
    private const string SubscribeEvents = "SlimFaas/SubscribeEvents";
    private const string DefaultVisibility = "SlimFaas/DefaultVisibility";
    private const string PathsStartWithVisibility = "SlimFaas/PathsStartWithVisibility";
    private const string ExcludeDeploymentsFromVisibilityPrivate = "SlimFaas/ExcludeDeploymentsFromVisibilityPrivate";

    private const string ReplicasStartAsSoonAsOneFunctionRetrieveARequest =
        "SlimFaas/ReplicasStartAsSoonAsOneFunctionRetrieveARequest";

    private const string TimeoutSecondBeforeSetReplicasMin = "SlimFaas/TimeoutSecondBeforeSetReplicasMin";
    private const string NumberParallelRequest = "SlimFaas/NumberParallelRequest";
    private const string SlimfaasDeploymentName = "slimfaas";
    private readonly ILogger<KubernetesService> _logger;
    private readonly k8s.Kubernetes _client;

    public KubernetesService(ILogger<KubernetesService> logger, bool useKubeConfig)
    {
        _logger = logger;
        KubernetesClientConfiguration k8SConfig = !useKubeConfig
            ? KubernetesClientConfiguration.InClusterConfig()
            : KubernetesClientConfiguration.BuildConfigFromConfigFile();
        k8SConfig.SkipTlsVerify = true;
        _client = new k8s.Kubernetes(k8SConfig);
    }


    public async Task<ReplicaRequest?> ScaleAsync(ReplicaRequest request)
    {
        try
        {
            var client = _client;
            string patchString = $"{{\"spec\": {{\"replicas\": {request.Replicas}}}}}";
            var httpContent = new StringContent(patchString, Encoding.UTF8, "application/merge-patch+json");
            // we need to get the base uri, as it's not set on the HttpClient
            switch (request.PodType)
            {
                case PodType.Deployment:
                    {
                        var url = string.Concat(client.BaseUri, $"apis/apps/v1/namespaces/{request.Namespace}/deployments/{request.Deployment}/scale" );
                        HttpRequestMessage httpRequest = new(HttpMethod.Patch,
                            new Uri(url));
                        httpRequest.Content = httpContent;
                        if ( client.Credentials != null )
                        {
                            await client.Credentials.ProcessHttpRequestAsync( httpRequest, CancellationToken.None );
                        }
                        var response = await client.HttpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
                        if(response.StatusCode != HttpStatusCode.OK)
                        {
                            throw new HttpOperationException("Error while scaling deployment");
                        }
                        break;
                    }
                case PodType.StatefulSet:
                    {
                        var url = string.Concat(client.BaseUri, $"apis/apps/v1/namespaces/{request.Namespace}/statefulsets/{request.Deployment}/scale" );
                        HttpRequestMessage httpRequest = new(HttpMethod.Patch,
                            new Uri(url));
                        httpRequest.Content = httpContent;
                        if ( client.Credentials != null )
                        {
                            await client.Credentials.ProcessHttpRequestAsync( httpRequest, CancellationToken.None );
                        }
                        var response = await client.HttpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead );
                        if(response.StatusCode != HttpStatusCode.OK)
                        {
                            throw new HttpOperationException("Error while scaling deployment");
                        }
                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException(request.PodType.ToString());
            }
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
            var client = _client;
            IList<DeploymentInformation>? deploymentInformationList = new List<DeploymentInformation>();

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

            IEnumerable<PodInformation> podInformations = podList.ToArray();
            AddDeployments(kubeNamespace, deploymentList, podInformations, deploymentInformationList, _logger);
            AddStatefulSets(kubeNamespace, statefulSetList, podInformations, deploymentInformationList, _logger);

            return new DeploymentsInformations(deploymentInformationList,
                slimFaasDeploymentInformation ?? new SlimFaasDeploymentInformation(1, new List<PodInformation>()), podInformations);
        }
        catch (HttpOperationException e)
        {
            _logger.LogError(e, "Error while listing kubernetes functions");
            return new DeploymentsInformations(new List<DeploymentInformation>(),
                new SlimFaasDeploymentInformation(1, new List<PodInformation>()), new List<PodInformation>());
        }
    }

    private static void AddDeployments(string kubeNamespace, V1DeploymentList deploymentList, IEnumerable<PodInformation> podList,
        IList<DeploymentInformation> deploymentInformationList, ILogger<KubernetesService> logger)
    {
        foreach (V1Deployment? deploymentListItem in deploymentList.Items)
        {
            try
            {
                var annotations = deploymentListItem.Spec.Template?.Metadata?.Annotations;
                if (annotations == null || !annotations.ContainsKey(Function) ||
                    annotations[Function].ToLower() != "true")
                {
                    continue;
                }

                var name = deploymentListItem.Metadata.Name;
                ScheduleConfig? scheduleConfig = GetScheduleConfig(annotations, name, logger);

                DeploymentInformation deploymentInformation = new(
                    name,
                    kubeNamespace,
                    podList.Where(p => p.DeploymentName.StartsWith(name)).ToList(),
                    deploymentListItem.Spec.Replicas ?? 0,
                    annotations.TryGetValue(ReplicasAtStart, out string? annotationReplicasAtStart)
                        ? int.Parse(annotationReplicasAtStart)
                        : 1, annotations.TryGetValue(ReplicasMin, out string? annotationReplicaMin)
                        ? int.Parse(annotationReplicaMin)
                        : 0, annotations.TryGetValue(TimeoutSecondBeforeSetReplicasMin,
                        out string? annotationTimeoutSecondBeforeSetReplicasMin)
                        ? int.Parse(annotationTimeoutSecondBeforeSetReplicasMin)
                        : 300, annotations.TryGetValue(NumberParallelRequest,
                        out string? annotationNumberParallelRequest)
                        ? int.Parse(annotationNumberParallelRequest)
                        : 10, annotations.ContainsKey(
                                  ReplicasStartAsSoonAsOneFunctionRetrieveARequest) &&
                              annotations[ReplicasStartAsSoonAsOneFunctionRetrieveARequest].ToLower() == "true",
                    PodType.Deployment,
                    annotations.TryGetValue(DependsOn, out string? value)
                        ? value.Split(',').ToList()
                        : new List<string>(),
                    scheduleConfig,
                    annotations.TryGetValue(SubscribeEvents, out string? valueSubscribeEvents)
                        ? valueSubscribeEvents.Split(',').ToList()
                        : new List<string>(),
                    annotations.TryGetValue(DefaultVisibility, out string? visibility)
                        ? Enum.Parse<FunctionVisibility>(visibility)
                        : FunctionVisibility.Public,
                    annotations.TryGetValue(PathsStartWithVisibility, out string? valueUrlsStartWithVisibility)
                        ? valueUrlsStartWithVisibility.Split(',').ToList()
                        : new List<string>(),
                    annotations.TryGetValue(ExcludeDeploymentsFromVisibilityPrivate, out string? valueExcludeDeploymentsFromVisibilityPrivate) ? valueExcludeDeploymentsFromVisibilityPrivate.Split(',').ToList() : new List<string>(),
                    deploymentListItem.Metadata.ResourceVersion
                    );
                deploymentInformationList.Add(deploymentInformation);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while adding deployment {Deployment}", deploymentListItem.Metadata.Name);
            }
        }
    }

    private static ScheduleConfig? GetScheduleConfig(IDictionary<string, string> annotations, string name, ILogger<KubernetesService> logger)
    {
        try
        {
            if (annotations.TryGetValue(Schedule, out string? annotation) && !string.IsNullOrEmpty(annotation.Trim()))
            {
               return JsonSerializer.Deserialize(annotation, ScheduleConfigSerializerContext.Default.ScheduleConfig);
            }
        }
        catch (Exception e)
        {
            logger.LogError( e, "name: {Name}\\n annotations[Schedule]: {Annotation}", name, annotations[Schedule]);
        }

        return new ScheduleConfig();
    }

    private static void AddStatefulSets(string kubeNamespace, V1StatefulSetList deploymentList, IEnumerable<PodInformation> podList,
        IList<DeploymentInformation> deploymentInformationList, ILogger<KubernetesService> logger)
    {
        foreach (V1StatefulSet? deploymentListItem in deploymentList.Items)
        {
            try
            {
                IDictionary<string, string>? annotations = deploymentListItem.Spec.Template?.Metadata?.Annotations;
                if (annotations == null || !annotations.ContainsKey(Function) ||
                    annotations[Function].ToLower() != "true")
                {
                    continue;
                }

                var name = deploymentListItem.Metadata.Name;
                ScheduleConfig? scheduleConfig = GetScheduleConfig(annotations, name, logger);

                DeploymentInformation deploymentInformation = new(
                    name,
                    kubeNamespace,
                    podList.Where(p => p.DeploymentName.StartsWith(name)).ToList(),
                    deploymentListItem.Spec.Replicas ?? 0,
                    annotations.TryGetValue(ReplicasAtStart, out string? annotationReplicasAtStart)
                        ? int.Parse(annotationReplicasAtStart)
                        : 1, annotations.TryGetValue(ReplicasMin, out string? annotationReplicasMin)
                        ? int.Parse(annotationReplicasMin)
                        : 0, annotations.TryGetValue(TimeoutSecondBeforeSetReplicasMin,
                        out string? annotationTimeoutSecondBeforeSetReplicasMin)
                        ? int.Parse(annotationTimeoutSecondBeforeSetReplicasMin)
                        : 300, annotations.TryGetValue(NumberParallelRequest,
                        out string? annotationNumberParallelRequest)
                        ? int.Parse(annotationNumberParallelRequest)
                        : 10, annotations.ContainsKey(
                                  ReplicasStartAsSoonAsOneFunctionRetrieveARequest) &&
                              annotations[ReplicasStartAsSoonAsOneFunctionRetrieveARequest].ToLower() == "true",
                    PodType.StatefulSet,
                    annotations.TryGetValue(DependsOn, out string? value)
                        ? value.Split(',').ToList()
                        : new List<string>(),
                    scheduleConfig,
                    annotations.TryGetValue(SubscribeEvents, out string? valueSubscribeEvents)
                        ? valueSubscribeEvents.Split(',').ToList()
                        : new List<string>(),
                    annotations.TryGetValue(DefaultVisibility, out string? visibility)
                        ? Enum.Parse<FunctionVisibility>(visibility)
                        : FunctionVisibility.Public,
                    annotations.TryGetValue(PathsStartWithVisibility, out string? valueUrlsStartWithVisibility)
                        ? valueUrlsStartWithVisibility.Split(',').ToList()
                        : new List<string>(),
                    annotations.TryGetValue(ExcludeDeploymentsFromVisibilityPrivate, out string? valueExcludeDeploymentsFromVisibilityPrivate) ? valueExcludeDeploymentsFromVisibilityPrivate.Split(',').ToList() : new List<string>(),
                    deploymentListItem.Metadata.ResourceVersion);

                deploymentInformationList.Add(deploymentInformation);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while adding statefulset {Deployment}", deploymentListItem.Metadata.Name);
            }
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

            PodInformation podInformation = new(podName, started, ready, podIp, deploymentName);
            yield return podInformation;
        }
    }

    public static string ExtractPodDeploymentNameFrom(string generalName)
    {
        string[] names = generalName.Split('-');
        if (names.Length <= 1)
        {
            return generalName;
        }

        StringBuilder realName = new(names[0]);
        for (int i = 1; i < names.Length - 2; i++)
        {
            realName.Append($"-{names[i]}");
        }

        return realName.ToString();
    }

}
