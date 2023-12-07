using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using SlimFaas.Kubernetes;

namespace SlimFaas;

[ExcludeFromCodeCoverage]
public record struct FunctionsMock
{
    public List<FunctionMock> Functions { get; set; }
    public List<SlimfaasMock> Slimfaas { get; set; }
}

[ExcludeFromCodeCoverage]
public record struct FunctionMock
{
    public int NumberParallelRequest { get; set; }

    public string Name { get; set; }
}

[ExcludeFromCodeCoverage]
public record struct SlimfaasMock
{
    public string Name { get; set; }
}

[ExcludeFromCodeCoverage]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(FunctionsMock))]
[JsonSerializable(typeof(FunctionMock))]
[JsonSerializable(typeof(List<FunctionMock>))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal class FunctionsMockSerializerContext : JsonSerializerContext
{
}

[ExcludeFromCodeCoverage]
public class MockKubernetesService : IKubernetesService
{
    private readonly DeploymentsInformations _deploymentInformations;

    public MockKubernetesService()
    {
        string functionsJson = Environment.GetEnvironmentVariable(EnvironmentVariables.MockKubernetesFunctions) ??
                               EnvironmentVariables.MockKubernetesFunctionsDefault;
        object? functions =
            JsonSerializer.Deserialize(functionsJson, FunctionsMockSerializerContext.Default.FunctionsMock);
        List<PodInformation> slimfaasPods = new List<PodInformation>();
        foreach (var pod in functions.Slimfaas)
        {
            slimfaasPods.Add(new PodInformation(pod.Name, true, true, "localhost", "slimfaas"));
        }

        _deploymentInformations = new DeploymentsInformations(new List<DeploymentInformation>(),
            new SlimFaasDeploymentInformation(1, slimfaasPods));

        foreach (var function in functions.Functions)
        {
            DeploymentInformation deploymentInformation = new DeploymentInformation(function.Name, Replicas: 1,
                ReplicasMin: 1, ReplicasAtStart: 1, TimeoutSecondBeforeSetReplicasMin: 1000000,
                Namespace: "default",
                ReplicasStartAsSoonAsOneFunctionRetrieveARequest: false,
                NumberParallelRequest: function.NumberParallelRequest,
                Pods: new List<PodInformation> { new("", true, true, "", "") }
            );
            _deploymentInformations.Functions.Add(deploymentInformation);
        }
    }

    public Task<ReplicaRequest?> ScaleAsync(ReplicaRequest? request) => Task.FromResult(request);

    public Task<DeploymentsInformations> ListFunctionsAsync(string kubeNamespace) =>
        Task.FromResult(_deploymentInformations);
}
