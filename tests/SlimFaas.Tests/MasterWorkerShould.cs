using Microsoft.Extensions.Logging;
using Moq;

namespace SlimFaas.Tests;

public class MasterWorkerShould
{
     [Fact]
    public async Task BecomeTheMaster()
    {
        var logger = new Mock<ILogger<MasterWorker>>();
        var redisMockService = new RedisMockService();
        var masterService = new MasterService(redisMockService);
        var service = new MasterWorker(masterService, logger.Object, 100);

        var task = service.StartAsync(CancellationToken.None);

        await Task.Delay(300);

        Assert.True(task.IsCompleted);
    }
}