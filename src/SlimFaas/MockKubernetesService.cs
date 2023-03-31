namespace SlimFaas;

public class MockKubernetesService : IKubernetesService
{

    private readonly IList<DeploymentInformation>? _deploymentInformations = new List<DeploymentInformation>();
    public MockKubernetesService()
    {
        var functions = Environment.GetEnvironmentVariable("MOCK_KUBERNETES_FUNCTIONS").Split(":") ?? new string[0];
        
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
            _deploymentInformations.Add(deploymentInformation);
        }
    }
    public Task<ReplicaRequest?> ScaleAsync(ReplicaRequest? request)
    {
        return Task.FromResult(request);
    }

    public Task<IList<DeploymentInformation>?> ListFunctionsAsync(string kubeNamespace)
    {
        return Task.FromResult(_deploymentInformations);
    }
}