using SlimFaas.Kubernetes;

namespace SlimFaas.Tests;

public class KubernetesServiceShould
{
    [Theory]
    [InlineData("fibonacci-1-0f161655-0f61131", "fibonacci-1")]
    [InlineData("fibonacci-test-2-05f61655-0f61131", "fibonacci-test-2")]
    [InlineData("finbonacci-051616f5-0f61131", "finbonacci")]
    [InlineData("redis-ha-server-1", "redis-ha-server")]
    public void ExtractPodDeploymentNameFrom(string generalName, string expectedName)
    {
        string name = KubernetesService.ExtractPodDeploymentNameFrom(generalName);
        Assert.Equal(expectedName, name);
    }
}
