using System.Collections;
using Microsoft.Extensions.Logging;
using Moq;

namespace SlimFaas.Tests;

public class DeploymentsTestData:IEnumerable<object[]>
{
    public IEnumerator<object[]> GetEnumerator()
    {
        yield return new object[] { new DeploymentsInformations() { Functions = new List<DeploymentInformation>()} };
        yield return new object[] { new DeploymentsInformations()
        {
            Functions = new List<DeploymentInformation>()
            {
                new() { Deployment = "fibonacci1", Namespace = "default", Replicas = 1},
                new() { Deployment = "fibonacci2", Namespace = "default", Replicas = 0}
            }
        } };
    }
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

public class ReplicasSynchronizationWorkerShould
{
    
    [Theory]
    [ClassData(typeof(DeploymentsTestData))]
    public async Task SynchroniseDeployments(DeploymentsInformations deploymentsInformations)
    {
        var logger = new Mock<ILogger<ReplicasSynchronizationWorker>>();
        var kubernetesService = new Mock<IKubernetesService>();
        kubernetesService.Setup(k=>k.ListFunctionsAsync(It.IsAny<string>())).ReturnsAsync(deploymentsInformations);
        var masterService = new Mock<IMasterService>();
        var historyHttpService = new HistoryHttpMemoryService();
        var replicasService = new ReplicasService(kubernetesService.Object, historyHttpService);
        masterService.Setup(ms => ms.IsMaster).Returns(true); 
        
        var service = new ReplicasSynchronizationWorker(replicasService, logger.Object, 100);
        var task = service.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        
        Assert.True(task.IsCompleted);
        kubernetesService.Verify(v => v.ListFunctionsAsync(It.IsAny<string>()));
    }
    
}