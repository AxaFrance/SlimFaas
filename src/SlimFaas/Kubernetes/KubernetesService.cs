using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
    string ResourceVersion = "",
    IList<int>? AsynchrounousRetry = null,
    IList<int>? SynchrounousRetry = null,
    bool EndpointReady = false
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
    private const string SynchrounousRetry = "SlimFaas/SynchrounousRetry";
    private const string AsynchrounousRetry = "SlimFaas/AsynchrounousRetry";


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


    public async Task<DeploymentsInformations> ListFunctionsAsync(string kubeNamespace, DeploymentsInformations previousDeployments)
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
            IEnumerable<PodInformation> podList = await MapPodInformations(podListTask.Result);
            V1StatefulSetList? statefulSetList = statefulSetListTask.Result;

            SlimFaasDeploymentInformation? slimFaasDeploymentInformation = statefulSetList.Items
                .Where(deploymentListItem => deploymentListItem.Metadata.Name == SlimfaasDeploymentName).Select(
                    deploymentListItem =>
                        new SlimFaasDeploymentInformation(deploymentListItem.Spec.Replicas ?? 0,
                            podList.Where(p => p.Name.StartsWith(deploymentListItem.Metadata.Name)).ToList()))
                .FirstOrDefault();

            IEnumerable<PodInformation> podInformations = podList.ToArray();
            await AddDeployments(kubeNamespace, deploymentList, podInformations, deploymentInformationList, _logger, client, previousDeployments.Functions);
            await AddStatefulSets(kubeNamespace, statefulSetList, podInformations, deploymentInformationList, _logger, client, previousDeployments.Functions);

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

    private static IList<int> ReadRetryAnnotation( IDictionary<string, string>? annotations)
    {
        if (annotations != null && annotations.TryGetValue(AsynchrounousRetry, out string? valueAsynchrounousRetry))
        {
            var result = new List<int>();
            var array = valueAsynchrounousRetry.Split(';');
            foreach (string s in array)
            {
                result.Add( int.Parse(s, NumberStyles.Integer));
            }

            return result;
        }

        return new List<int>(){2, 4, 8};
    }

    private static async Task AddDeployments(string kubeNamespace, V1DeploymentList deploymentList, IEnumerable<PodInformation> podList,
        IList<DeploymentInformation> deploymentInformationList, ILogger<KubernetesService> logger, k8s.Kubernetes client , IList<DeploymentInformation> previousDeploymentInformationList)
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
                var pods = podList.Where(p => p.DeploymentName.StartsWith(name)).ToList();
                ScheduleConfig? scheduleConfig = GetScheduleConfig(annotations, name, logger);
                bool endpointReady = await GetEndpointReady(kubeNamespace, client, previousDeploymentInformationList, name, pods);

                DeploymentInformation deploymentInformation = new(
                    name,
                    kubeNamespace,
                    pods,
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
                    deploymentListItem.Metadata.ResourceVersion,
                    AsynchrounousRetry: ReadRetryAnnotation(annotations),
                    SynchrounousRetry: ReadRetryAnnotation(annotations),
                    EndpointReady: endpointReady
                    );
                deploymentInformationList.Add(deploymentInformation);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while adding deployment {Deployment}", deploymentListItem.Metadata.Name);
            }
        }
    }

    private static async Task<bool> GetEndpointReady(string kubeNamespace, k8s.Kubernetes client,
        IList<DeploymentInformation> previousDeploymentInformationList, string name, List<PodInformation> pods)
    {
        try
        {
            if (pods.Count == 0)
            {
                return false;
            }

            var previousDeployment = previousDeploymentInformationList.FirstOrDefault(d => d.Deployment == name);
            if (previousDeployment is not { EndpointReady: false } || pods.Count != 1)
            {
                return previousDeployment is { EndpointReady: true };
            }

            var endpoints = await client.CoreV1.ReadNamespacedEndpointsAsync(name, kubeNamespace);
            if (endpoints is not { Subsets: not null })
            {
                return previousDeployment is { EndpointReady: true };
            }

            var readyAddresses = endpoints.Subsets
                .Where(s => s.Addresses != null)
                .SelectMany(s => s.Addresses)
                .ToList();
            return readyAddresses.Count > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
        return false;
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

    private static async Task AddStatefulSets(string kubeNamespace, V1StatefulSetList deploymentList, IEnumerable<PodInformation> podList,
        IList<DeploymentInformation> deploymentInformationList, ILogger<KubernetesService> logger, k8s.Kubernetes client , IList<DeploymentInformation> previousDeploymentInformationList)
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
                var pods = podList.Where(p => p.DeploymentName.StartsWith(name)).ToList();
                ScheduleConfig? scheduleConfig = GetScheduleConfig(annotations, name, logger);
                bool endpointReady = await GetEndpointReady(kubeNamespace, client, previousDeploymentInformationList, name, pods);

                DeploymentInformation deploymentInformation = new(
                    name,
                    kubeNamespace,
                    pods,
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
                    deploymentListItem.Metadata.ResourceVersion,
                    AsynchrounousRetry: ReadRetryAnnotation(annotations),
                    SynchrounousRetry: ReadRetryAnnotation(annotations),
                    EndpointReady: endpointReady);

                deploymentInformationList.Add(deploymentInformation);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while adding statefulset {Deployment}", deploymentListItem.Metadata.Name);
            }
        }
    }

    private static async Task<IEnumerable<PodInformation>> MapPodInformations(V1PodList v1PodList)
    {
        var result = new List<PodInformation>();
        foreach (V1Pod? item in v1PodList.Items)
        {
            string? podIp = item.Status.PodIP;
            if (string.IsNullOrEmpty(podIp))
            {
                continue;
            }

            V1ContainerStatus? containerStatus = item.Status.ContainerStatuses.FirstOrDefault();
            bool started = containerStatus?.Started ?? false;
            bool containerReady = item.Status.Conditions.FirstOrDefault(c => c.Type == "ContainersReady")?.Status == "True";
            bool podReady = item.Status.Conditions.FirstOrDefault(c => c.Type == "Ready")?.Status == "True";
            string? podName = item.Metadata.Name;
            string deploymentName = item.Metadata.OwnerReferences[0].Name;

            PodInformation podInformation = new(podName, started, containerReady && podReady, podIp, deploymentName);
            result.Add(podInformation);
        }
        return result;
    }


}
