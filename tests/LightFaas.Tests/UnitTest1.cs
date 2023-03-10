using k8s;

namespace LightFaas.Tests;

public class UnitTest1
{
    [Fact]
    public void Test1()
    {
        // Load from the default kubeconfig on the machine.
        var config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
        // Load from a specific file:
        //var config = KubernetesClientConfiguration.BuildConfigFromConfigFile(Environment.GetEnvironmentVariable("KUBECONFIG"));

        // Load from in-cluster configuration:
        //var config = KubernetesClientConfiguration.InClusterConfig()

        // Use the config object to create a client.
        var client = new Kubernetes(config);

        var namespaceList = client.ListNamespace();
        
        var podList = client.ListNamespacedPod("license-preproduction");

        var deploymentList = client.ListNamespacedDeployment("license-preproduction");
        
        Assert.NotNull(podList);
    }
}