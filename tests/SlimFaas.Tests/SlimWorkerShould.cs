using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SlimFaas.Kubernetes;

namespace SlimFaas.Tests;

public class SlimWorkerShould
{
    [Fact]
    public async Task OnlyCallOneFunctionAsync()
    {
        var responseMessage = new HttpResponseMessage();
        responseMessage.StatusCode = HttpStatusCode.OK;
        
        var sendClientMock = new Mock<ISendClient>();
        sendClientMock.Setup(s => s.SendHttpRequestAsync(It.IsAny<CustomRequest>(), It.IsAny<HttpContext>()))
            .ReturnsAsync(responseMessage);
        
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(x => x.GetService(typeof(ISendClient)))
            .Returns(sendClientMock.Object);

        var serviceScope = new Mock<IServiceScope>();
        serviceScope.Setup(x => x.ServiceProvider).Returns(serviceProvider.Object);

        var serviceScopeFactory = new Mock<IServiceScopeFactory>();
        serviceScopeFactory
            .Setup(x => x.CreateScope())
            .Returns(serviceScope.Object);

        serviceProvider
            .Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(serviceScopeFactory.Object);

        var replicasService = new Mock<IReplicasService>();
        replicasService.Setup(rs => rs.Deployments).Returns(new DeploymentsInformations(
            SlimFaas: new SlimFaasDeploymentInformation(Replicas: 2),
            Functions: new List<DeploymentInformation>()
            {
                new(Replicas: 1, Deployment: "fibonacci", Namespace: "default", NumberParallelRequest: 1,
                    ReplicasMin: 0, ReplicasAtStart: 1, TimeoutSecondBeforeSetReplicasMin: 300,
                    ReplicasStartAsSoonAsOneFunctionRetrieveARequest: true,
                    Pods: new List<PodInformation>() { new("", true, true, "", "") }),
                new(Replicas: 1, Deployment: "no-pod-started", Namespace: "default", NumberParallelRequest: 1,
                    ReplicasMin: 0, ReplicasAtStart: 1, TimeoutSecondBeforeSetReplicasMin: 300,
                    ReplicasStartAsSoonAsOneFunctionRetrieveARequest: true,
                    Pods: new List<PodInformation>() { new("", false, false, "", "") }),
                new(Replicas: 0, Deployment: "no-replicas", Namespace: "default", NumberParallelRequest: 1,
                    ReplicasMin: 0, ReplicasAtStart: 1, TimeoutSecondBeforeSetReplicasMin: 300,
                    ReplicasStartAsSoonAsOneFunctionRetrieveARequest: true, Pods: new List<PodInformation>())
            }));
        var historyHttpService = new HistoryHttpMemoryService();
        var logger = new Mock<ILogger<SlimWorker>>();
        
        var redisQueue = new RedisQueue(new RedisMockService());
        var customRequest = new CustomRequest(new List<CustomHeader> { new() { Key = "key", Values = new []{"value1"}}}, new byte[1], "fibonacci", "/download", "GET", "");
        var jsonCustomRequest =
            JsonSerializer.Serialize(customRequest, CustomRequestSerializerContext.Default.CustomRequest);
        await redisQueue.EnqueueAsync("fibonacci", jsonCustomRequest);
        
        var customRequestNoPodStarted = new CustomRequest(new List<CustomHeader> { new() { Key = "key", Values = new []{"value1"}}}, new byte[1], "no-pod-started", "/download", "GET", "");
        var jsonCustomNoPodStarted =
            JsonSerializer.Serialize(customRequestNoPodStarted, CustomRequestSerializerContext.Default.CustomRequest);
        await redisQueue.EnqueueAsync("no-pod-started", jsonCustomNoPodStarted);
        
        var customRequestReplicas = new CustomRequest(new List<CustomHeader> { new() { Key = "key", Values = new []{"value1"}}}, new byte[1], "no-replicas", "/download", "GET", "");
        var jsonCustomNoReplicas =
            JsonSerializer.Serialize(customRequestReplicas, CustomRequestSerializerContext.Default.CustomRequest);
        await redisQueue.EnqueueAsync("no-replicas", jsonCustomNoReplicas);
        
        var service = new SlimWorker(redisQueue, 
            replicasService.Object, 
            historyHttpService, 
            logger.Object, 
            serviceProvider.Object);

        var task = service.StartAsync(CancellationToken.None);

        await Task.Delay(3000);

        Assert.True(task.IsCompleted);
        sendClientMock.Verify(v => v.SendHttpRequestAsync(It.IsAny<CustomRequest>(), It.IsAny<HttpContext>()), Times.Once());
    }
    
        [Fact]
    public async Task LogErrorWhenExceptionIsThrown()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        var replicasService = new Mock<IReplicasService>();
        replicasService.Setup(rs => rs.Deployments).Throws(new Exception());
        var historyHttpService = new HistoryHttpMemoryService();
        var logger = new Mock<ILogger<SlimWorker>>();
        var redisQueue = new RedisQueue(new RedisMockService());

        var service = new SlimWorker(redisQueue, 
            replicasService.Object, 
            historyHttpService, 
            logger.Object, 
            serviceProvider.Object);

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