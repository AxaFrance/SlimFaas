namespace SlimFaas;

public class MockKubernetesService : IKubernetesService
{

    private readonly DeploymentsInformations? _deploymentInformations;
    public MockKubernetesService()
    {
        var functions = Environment.GetEnvironmentVariable("MOCK_KUBERNETES_FUNCTIONS").Split(":") ?? new string[0];

        _deploymentInformations = new DeploymentsInformations()
        {
            Functions = new List<DeploymentInformation>(),
            SlimFaas = new SlimFaasDeploymentInformation()
            {
                Replicas = 1,
            }
        };
        foreach (var function in functions)
        {
            var deploymentInformation = new DeploymentInformation
            {
                Deployment = function,
                Replicas = 1,
                ReplicasMin = 1,
                ReplicasAtStart = 1,
                TimeoutSecondBeforeSetReplicasMin = 1000000,
                ReplicasStartAsSoonAsOneFunctionRetrieveARequest = false
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