using Microsoft.Extensions.Logging;
using Moq;

namespace SlimFaas.Tests;

public class HistorySynchronizationWorkerShould
{
    [Fact]
    public async Task SynLastTicksBetweenDatabaseAndMemory()
    {
        var logger = new Mock<ILogger<HistorySynchronizationWorker>>();
        var redisMockService = new RedisMockService();
        var historyHttpRedisService = new HistoryHttpRedisService(redisMockService);
        var kubernetesService = new Mock<IKubernetesService>();
        var deploymentsInformations = new DeploymentsInformations()
        {
            Functions = new List<DeploymentInformation>()
            {
                new() { Deployment = "fibonacci1", Namespace = "default", Replicas = 1},
                new() { Deployment = "fibonacci2", Namespace = "default", Replicas = 0}
            }
        } ;
        kubernetesService.Setup(k => k.ListFunctionsAsync(It.IsAny<string>())).ReturnsAsync(deploymentsInformations);
        var historyHttpMemoryService = new HistoryHttpMemoryService();
        var replicasService = new ReplicasService(kubernetesService.Object, historyHttpMemoryService);
        await replicasService.SyncDeploymentsAsync("default");
        
        var firstTicks = 1L;
        await historyHttpRedisService.SetTickLastCallAsync("fibonacci1", firstTicks);
        var service = new HistorySynchronizationWorker(replicasService, historyHttpMemoryService, historyHttpRedisService, logger.Object, 100);

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
}