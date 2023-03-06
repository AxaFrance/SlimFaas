namespace SlimFaas;

public interface IKubernetesService
{
    Task<ReplicaRequest?> ScaleAsync(ReplicaRequest? request);
    Task<DeploymentsInformations> ListFunctionsAsync(string kubeNamespace);
}