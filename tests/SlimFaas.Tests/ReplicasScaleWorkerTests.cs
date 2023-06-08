using Microsoft.Extensions.Logging;
using Moq;

namespace SlimFaas.Tests;
/*
public class MasterWorkerTests
{
     [Fact]
    public async Task WorkerShouldCallOneFunctionAsync()
    {
        var logger = new Mock<ILogger<MasterWorker>>();
        var redisMockService = new RedisMockService();
        var masterService = new MasterService(redisMockService);
        var service = new MasterWorker(masterService, logger.Object);

        var task = service.StartAsync(CancellationToken.None);

        await Task.Delay(3000);

        Assert.True(task.IsCompleted);
    }
}*/