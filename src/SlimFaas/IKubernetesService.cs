namespace SlimFaas;

public interface IKubernetesService
{
    Task<ReplicaRequest> ScaleAsync(ReplicaRequest request);
    Task<IList<DeploymentInformation>> ListFunctionsAsync(string kubeNamespace);
}