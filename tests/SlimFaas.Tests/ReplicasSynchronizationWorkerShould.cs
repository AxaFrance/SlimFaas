using System.Collections;
using Microsoft.Extensions.Logging;
using Moq;
using SlimFaas.Kubernetes;

namespace SlimFaas.Tests;

public class DeploymentsTestData:IEnumerable<object[]>
{
    public IEnumerator<object[]> GetEnumerator()
    {
        yield return new object[] { new DeploymentsInformations(new List<DeploymentInformation>(),new SlimFaasDeploymentInformation(Replicas: 1, new List<PodInformation>())) };
        yield return new object[] {
            new DeploymentsInformations( new List<DeploymentInformation>()
                {
                    new(Deployment: "fibonacci1", Namespace: "default", Replicas: 1, Pods: new List<PodInformation>()),
                    new(Deployment: "fibonacci2", Namespace: "default", Replicas: 0, Pods: new List<PodInformation>())
                },
            new SlimFaasDeploymentInformation(Replicas: 1, new List<PodInformation>())
            )
        };
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
        var loggerReplicasService = new Mock<ILogger<ReplicasService>>();
        var replicasService = new ReplicasService(kubernetesService.Object, historyHttpService, loggerReplicasService.Object);
        masterService.Setup(ms => ms.IsMaster).Returns(true);

        var service = new ReplicasSynchronizationWorker(replicasService, logger.Object, 100);
        var task = service.StartAsync(CancellationToken.None);
        await Task.Delay(300);

        Assert.True(task.IsCompleted);
        kubernetesService.Verify(v => v.ListFunctionsAsync(It.IsAny<string>()));
    }

    [Fact]
    public async Task LogErrorWhenExceptionIsThrown()
    {
        var logger = new Mock<ILogger<ReplicasSynchronizationWorker>>();
        var kubernetesService = new Mock<IKubernetesService>();
        kubernetesService.Setup(k => k.ListFunctionsAsync(It.IsAny<string>())).Throws(new Exception());
        var masterService = new Mock<IMasterService>();
        var historyHttpService = new HistoryHttpMemoryService();
        var loggerReplicasService = new Mock<ILogger<ReplicasService>>();
        var replicasService = new ReplicasService(kubernetesService.Object, historyHttpService, loggerReplicasService.Object);
        masterService.Setup(ms => ms.IsMaster).Returns(true);

        var service = new ReplicasSynchronizationWorker(replicasService, logger.Object, 10);
        var task = service.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            (Func<It.IsAnyType, Exception?, string>) It.IsAny<object>()), Times.AtLeastOnce);

        Assert.True(task.IsCompleted);
    }

}
