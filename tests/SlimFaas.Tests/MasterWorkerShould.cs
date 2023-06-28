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
    
    [Fact]
    public async Task LogErrorWhenExceptionIsThrown()
    {
        var logger = new Mock<ILogger<MasterWorker>>();
        var redisMockService =  new Mock<IMasterService>();
        redisMockService.Setup(r => r.CheckAsync()).Throws(new Exception());
        var service = new MasterWorker(redisMockService.Object, logger.Object, 10);
        
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