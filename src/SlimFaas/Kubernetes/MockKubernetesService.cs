using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlimFaas;

[ExcludeFromCodeCoverage]
public record struct FunctionsMock
{
    public List<FunctionMock> Functions { get; set; }
}

[ExcludeFromCodeCoverage]
public record struct FunctionMock
{
    public int NumberParallelRequest { get; set; }
    
    public string Name { get; set; }
}

[ExcludeFromCodeCoverage]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(FunctionsMock))]
[JsonSerializable(typeof(FunctionMock))]
[JsonSerializable(typeof(List<FunctionMock>))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class FunctionsMockSerializerContext : JsonSerializerContext
{
    
}

[ExcludeFromCodeCoverage]
public class MockKubernetesService : IKubernetesService
{

    private readonly DeploymentsInformations? _deploymentInformations;
    public MockKubernetesService()
    {
        var functionsJson = Environment.GetEnvironmentVariable("MOCK_KUBERNETES_FUNCTIONS") ?? "";
        
        _deploymentInformations = new DeploymentsInformations()
        {
            Functions = new List<DeploymentInformation>(),
            SlimFaas = new SlimFaasDeploymentInformation()
            {
                Replicas = 1,
            }
        };
        var functions = JsonSerializer.Deserialize<FunctionsMock>(functionsJson, FunctionsMockSerializerContext.Default.FunctionsMock);
        foreach (var function in functions.Functions)
        {
            
            var deploymentInformation = new DeploymentInformation
            {
                Deployment = function.Name,
                Replicas = 1,
                ReplicasMin = 1,
                ReplicasAtStart = 1,
                TimeoutSecondBeforeSetReplicasMin = 1000000,
                ReplicasStartAsSoonAsOneFunctionRetrieveARequest = false,
                NumberParallelRequest = function.NumberParallelRequest,
                Pods = new List<PodInformation>()
                {
                    new("", true, true, "", "")
                }
            };
            _deploymentInformations.Functions.Add(deploymentInformation);
        }
    }
    public Task<ReplicaRequest?> ScaleAsync(ReplicaRequest? request)
    {
        return Task.FromResult(request);
    }

    public Task<DeploymentsInformations> ListFunctionsAsync(string kubeNamespace)
    {
        return Task.FromResult(_deploymentInformations);
    }
}