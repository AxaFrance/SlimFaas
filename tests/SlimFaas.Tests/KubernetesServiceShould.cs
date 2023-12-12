using SlimFaas.Kubernetes;

namespace SlimFaas.Tests;

public class KubernetesServiceShould
{
    [Theory]
    [InlineData("fibonacci-1-05161655-0561131", "fibonacci-1")]
    [InlineData("fibonacci-test-2-05161655-0561131", "fibonacci-test-2")]
    [InlineData("finbonacci-05161655-0561131", "finbonacci")]
    public void ExtractPodDeploymentNameFrom(string generalName, string expectedName)
    {
        string name = KubernetesService.ExtractPodDeploymentNameFrom(generalName);
        Assert.Equal(expectedName, name);
    }
}
