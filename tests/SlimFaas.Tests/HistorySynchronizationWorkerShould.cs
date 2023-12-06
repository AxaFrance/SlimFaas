using Microsoft.Extensions.Logging;
using Moq;
using SlimFaas.Kubernetes;

namespace SlimFaas.Tests;

public class HistorySynchronizationWorkerShould
{
    [Fact]
    public async Task SyncLastTicksBetweenDatabaseAndMemory()
    {
        var logger = new Mock<ILogger<HistorySynchronizationWorker>>();
        var redisMockService = new DatabaseMockService();
        var historyHttpRedisService = new HistoryHttpDatabaseService(redisMockService);
        var kubernetesService = new Mock<IKubernetesService>();
        var deploymentsInformations = new DeploymentsInformations(Functions: new List<DeploymentInformation>()
        {
            new(Deployment: "fibonacci1", Namespace: "default", Replicas: 1, Pods: new List<PodInformation>()),
            new(Deployment: "fibonacci2", Namespace: "default", Replicas: 0, Pods: new List<PodInformation>())
        },
            new SlimFaasDeploymentInformation(1, new List<PodInformation>()));
        kubernetesService.Setup(k => k.ListFunctionsAsync(It.IsAny<string>())).ReturnsAsync(deploymentsInformations);
        var historyHttpMemoryService = new HistoryHttpMemoryService();
        var loggerReplicasService = new Mock<ILogger<ReplicasService>>();
        var replicasService = new ReplicasService(kubernetesService.Object, historyHttpMemoryService, loggerReplicasService.Object);

        var slimDataStatus = new Mock<ISlimDataStatus>();
        slimDataStatus.Setup(s => s.WaitForReadyAsync()).Returns(Task.CompletedTask);

        await replicasService.SyncDeploymentsAsync("default");

        var firstTicks = 1L;
        await historyHttpRedisService.SetTickLastCallAsync("fibonacci1", firstTicks);
        var service = new HistorySynchronizationWorker(replicasService, historyHttpMemoryService, historyHttpRedisService, logger.Object ,slimDataStatus.Object, 100);

        var task = service.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        var ticksFirstCallAsync = historyHttpMemoryService.GetTicksLastCall("fibonacci1");
        Assert.Equal(firstTicks, ticksFirstCallAsync);

        var secondTicks = 2L;
        historyHttpMemoryService.SetTickLastCall("fibonacci1", secondTicks);
        await Task.Delay(200);
        var ticksSecondCallAsync = await historyHttpRedisService.GetTicksLastCallAsync("fibonacci1");
        Assert.Equal(secondTicks, ticksSecondCallAsync);

        Assert.True(task.IsCompleted);
    }

    [Fact]
    public async Task LogErrorWhenExceptionIsThrown()
    {
        var logger = new Mock<ILogger<HistorySynchronizationWorker>>();
        var redisMockService = new DatabaseMockService();
        var historyHttpRedisService = new HistoryHttpDatabaseService(redisMockService);
        var historyHttpMemoryService = new HistoryHttpMemoryService();
        var replicasService = new Mock<IReplicasService>();
        replicasService.Setup(r => r.Deployments).Throws(new Exception());
        var slimDataStatus = new Mock<ISlimDataStatus>();
        slimDataStatus.Setup(s => s.WaitForReadyAsync()).Returns(Task.CompletedTask);

        var service = new HistorySynchronizationWorker(replicasService.Object, historyHttpMemoryService, historyHttpRedisService, logger.Object, slimDataStatus.Object, 10);

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
