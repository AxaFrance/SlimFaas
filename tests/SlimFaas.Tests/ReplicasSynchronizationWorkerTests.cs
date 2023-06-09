using Microsoft.Extensions.Logging;
using Moq;

namespace SlimFaas.Tests;

public class ReplicasSynchronizationWorkerTests
{
    [Fact]  
    public async Task WorkerShouldCallOneFunctionAsync()
    {
        var logger = new Mock<ILogger<ReplicasSynchronizationWorker>>();
        var kubernetesService = new Mock<IKubernetesService>();
        kubernetesService.Setup(k=>k.ListFunctionsAsync(It.IsAny<string>())).ReturnsAsync(new DeploymentsInformations());
        var masterService = new Mock<IMasterService>();
        var historyHttpService = new HistoryHttpMemoryService();
        var replicasService = new ReplicasService(kubernetesService.Object, historyHttpService);
        
        masterService.Setup(ms => ms.IsMaster).Returns(true);
        
        var service = new ReplicasSynchronizationWorker(replicasService, logger.Object);

        var task = service.StartAsync(CancellationToken.None);

        await Task.Delay(3000);

        Assert.True(task.IsCompleted);
    }
}