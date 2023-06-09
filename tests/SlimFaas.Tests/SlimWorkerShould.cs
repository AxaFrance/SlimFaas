﻿using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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
        replicasService.Setup(rs => rs.Deployments).Returns(new DeploymentsInformations()
        {
            SlimFaas = new SlimFaasDeploymentInformation()
            {
                Replicas = 2
            },
            Functions = new List<DeploymentInformation>()
            {
                new()
                {
                    Replicas = 1,
                    Deployment = "fibonacci",
                    Namespace = "default",
                    NumberParallelRequest = 1,
                    ReplicasMin = 0,
                    ReplicasAtStart = 1,
                    TimeoutSecondBeforeSetReplicasMin = 300,
                    ReplicasStartAsSoonAsOneFunctionRetrieveARequest = true
                }
            }
        });
        var historyHttpService = new HistoryHttpMemoryService();
        var logger = new Mock<ILogger<SlimWorker>>();
        
        var redisQueue = new RedisQueue(new RedisMockService());
        var customRequest = new CustomRequest(new List<CustomHeader>(){}, new byte[1], "fibonacci", "/download", "GET", "");
        var jsonCustomRequest =
            JsonSerializer.Serialize(customRequest, CustomRequestSerializerContext.Default.CustomRequest);
        await redisQueue.EnqueueAsync("fibonacci", jsonCustomRequest);
        
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
}