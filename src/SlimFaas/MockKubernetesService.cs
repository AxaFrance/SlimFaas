using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlimFaas;


public record struct DeploymentInformationMock
{
    public int NumberParallelRequest { get; set; }
    
    public string Deployment { get; set; }
}

[JsonSerializable(typeof(DeploymentInformationMock))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class DeploymentInformationMockSerializerContext : JsonSerializerContext
{
    
}

public class MockKubernetesService : IKubernetesService
{

    private readonly DeploymentsInformations? _deploymentInformations;
    public MockKubernetesService()
    {
        var functionsJson = Environment.GetEnvironmentVariable("MOCK_KUBERNETES_FUNCTIONS").Split(";") ?? new string[0];
       

        _deploymentInformations = new DeploymentsInformations()
        {
            Functions = new List<DeploymentInformation>(),
            SlimFaas = new SlimFaasDeploymentInformation()
            {
                Replicas = 1,
            }
        };
        foreach (var functionJson in functionsJson)
        {
            var function = JsonSerializer.Deserialize(functionJson, DeploymentInformationMockSerializerContext.Default.DeploymentInformationMock);
            var deploymentInformation = new DeploymentInformation
            {
                Deployment = function.Deployment,
                Replicas = 1,
                ReplicasMin = 1,
                ReplicasAtStart = 1,
                TimeoutSecondBeforeSetReplicasMin = 1000000,
                ReplicasStartAsSoonAsOneFunctionRetrieveARequest = false,
                NumberParallelRequest = function.NumberParallelRequest,
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