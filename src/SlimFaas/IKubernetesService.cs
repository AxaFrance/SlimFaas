namespace SlimFaas;

public interface IKubernetesService
{
    Task ScaleAsync(ReplicaRequest request);
    Task<IList<DeploymentInformation>> ListFunctionsAsync(string kubeNamespace);
}