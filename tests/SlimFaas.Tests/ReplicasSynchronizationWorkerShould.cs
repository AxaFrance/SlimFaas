using System.Collections;
using Microsoft.Extensions.Logging;
using Moq;
using SlimFaas.Kubernetes;

namespace SlimFaas.Tests;

public class DeploymentsTestData : IEnumerable<object[]>
{
    public IEnumerator<object[]> GetEnumerator()
    {
        yield return new object[]
        {
            new DeploymentsInformations(new List<DeploymentInformation>(),
                new SlimFaasDeploymentInformation(1, new List<PodInformation>()))
        };
        yield return new object[]
        {
            new DeploymentsInformations(
                new List<DeploymentInformation>
                {
                    new("fibonacci1", "default", Replicas: 1, Pods: new List<PodInformation>()),
                    new("fibonacci2", "default", Replicas: 0, Pods: new List<PodInformation>())
                },
                new SlimFaasDeploymentInformation(1, new List<PodInformation>())
            )
        };
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class ReplicasSynchronizationWorkerShould
{
    [Theory]
    [ClassData(typeof(DeploymentsTestData))]
    public async Task SynchroniseDeployments(DeploymentsInformations deploymentsInformations)
    {
        Mock<ILogger<ReplicasSynchronizationWorker>> logger = new();
        Mock<IKubernetesService> kubernetesService = new();
        kubernetesService.Setup(k => k.ListFunctionsAsync(It.IsAny<string>())).ReturnsAsync(deploymentsInformations);
        Mock<IMasterService> masterService = new();
        HistoryHttpMemoryService historyHttpService = new();
        Mock<ILogger<ReplicasService>> loggerReplicasService = new();
        ReplicasService replicasService =
            new(kubernetesService.Object, historyHttpService, loggerReplicasService.Object);
        masterService.Setup(ms => ms.IsMaster).Returns(true);
        Mock<IMasterService> masterServiceMock = new();
        masterServiceMock.Setup(ms => ms.IsMaster).Returns(true);
        Mock<IDatabaseService> databaseServiceMock = new ();
        ReplicasSynchronizationWorker service = new(replicasService, masterServiceMock.Object, databaseServiceMock.Object, logger.Object, 100);
        Task task = service.StartAsync(CancellationToken.None);
        await Task.Delay(300);

        Assert.True(task.IsCompleted);
        kubernetesService.Verify(v => v.ListFunctionsAsync(It.IsAny<string>()));
    }

    [Fact]
    public async Task LogErrorWhenExceptionIsThrown()
    {
        Mock<ILogger<ReplicasSynchronizationWorker>> logger = new Mock<ILogger<ReplicasSynchronizationWorker>>();
        Mock<IKubernetesService> kubernetesService = new Mock<IKubernetesService>();
        kubernetesService.Setup(k => k.ListFunctionsAsync(It.IsAny<string>())).Throws(new Exception());
        Mock<IMasterService> masterService = new Mock<IMasterService>();
        masterService.Setup(ms => ms.IsMaster).Returns(true);
        HistoryHttpMemoryService historyHttpService = new HistoryHttpMemoryService();
        Mock<ILogger<ReplicasService>> loggerReplicasService = new Mock<ILogger<ReplicasService>>();
        ReplicasService replicasService =
            new ReplicasService(kubernetesService.Object,
                historyHttpService,
                loggerReplicasService.Object);
        masterService.Setup(ms => ms.IsMaster).Returns(true);

        Mock<IDatabaseService> databaseService = new Mock<IDatabaseService>();
        ReplicasSynchronizationWorker service = new ReplicasSynchronizationWorker(replicasService, masterService.Object, databaseService.Object, logger.Object, 10);
        Task task = service.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()), Times.AtLeastOnce);

        Assert.True(task.IsCompleted);
    }
}
