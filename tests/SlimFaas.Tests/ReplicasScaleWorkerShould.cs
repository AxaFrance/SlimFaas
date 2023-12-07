using Microsoft.Extensions.Logging;
using Moq;
using SlimFaas.Kubernetes;

namespace SlimFaas.Tests;

public class ReplicasScaleWorkerShould
{
    [Theory]
    [ClassData(typeof(DeploymentsTestData))]
    public async Task ScaleFunctionUpAndDown(DeploymentsInformations deploymentsInformations)
    {
        Mock<ILogger<ScaleReplicasWorker>> logger = new Mock<ILogger<ScaleReplicasWorker>>();
        Mock<IKubernetesService> kubernetesService = new Mock<IKubernetesService>();
        Mock<IMasterService> masterService = new Mock<IMasterService>();
        HistoryHttpMemoryService historyHttpService = new HistoryHttpMemoryService();
        historyHttpService.SetTickLastCall("fibonacci2", DateTime.Now.Ticks);
        Mock<ILogger<ReplicasService>> loggerReplicasService = new Mock<ILogger<ReplicasService>>();
        ReplicasService replicasService =
            new ReplicasService(kubernetesService.Object, historyHttpService, loggerReplicasService.Object);
        masterService.Setup(ms => ms.IsMaster).Returns(true);
        kubernetesService.Setup(k => k.ListFunctionsAsync(It.IsAny<string>())).ReturnsAsync(deploymentsInformations);

        ReplicaRequest scaleRequestFibonacci1 = new ReplicaRequest("fibonacci1", "default", 0);
        kubernetesService.Setup(k => k.ScaleAsync(scaleRequestFibonacci1)).ReturnsAsync(scaleRequestFibonacci1);
        ReplicaRequest scaleRequestFibonacci2 = new ReplicaRequest("fibonacci2", "default", 1);
        kubernetesService.Setup(k => k.ScaleAsync(scaleRequestFibonacci2)).ReturnsAsync(scaleRequestFibonacci2);
        await replicasService.SyncDeploymentsAsync("default");

        ScaleReplicasWorker service =
            new ScaleReplicasWorker(replicasService, masterService.Object, logger.Object, 100);
        Task task = service.StartAsync(CancellationToken.None);
        await Task.Delay(300);

        Assert.True(task.IsCompleted);
    }

    [Fact]
    public async Task LogErrorWhenExceptionIsThrown()
    {
        Mock<ILogger<ScaleReplicasWorker>> logger = new Mock<ILogger<ScaleReplicasWorker>>();
        Mock<IMasterService> masterService = new Mock<IMasterService>();
        masterService.Setup(ms => ms.IsMaster).Returns(true);
        Mock<IReplicasService> replicaService = new Mock<IReplicasService>();
        replicaService.Setup(r => r.CheckScaleAsync(It.IsAny<string>())).Throws(new Exception());

        HistoryHttpMemoryService historyHttpService = new HistoryHttpMemoryService();
        historyHttpService.SetTickLastCall("fibonacci2", DateTime.Now.Ticks);

        ScaleReplicasWorker service =
            new ScaleReplicasWorker(replicaService.Object, masterService.Object, logger.Object, 10);
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
