using k8s;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace SlimFaas.Tests;

public class UnitTest1
{
    [Fact]
    public async Task Test1()
    {
        
        
        RedisService _redisService = new RedisService();
        var dictionary= _redisService.HashGetAll("lightfaas_master");

        if (dictionary.Count > 0)
        {
            var currentMasterId = dictionary["master_id"];
            var currentTicks = long.Parse(dictionary["last_ticks"]);
        }
        else
        {
            _redisService.HashSet("lightfaas_master", new Dictionary<string, string>()
            {
                { "master_id", "youhou" },
                { "last_ticks",  DateTime.Now.Ticks.ToString() },
            });
        }

        //Arrange
        var inMemorySettings = new Dictionary<string, string> {
            {"UseKubeConfig", "true"},
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
        var kubernetesService = new KubernetesService(configuration);
        var functions = await kubernetesService.ListFunctionsAsync("lightfaas-demo");
        
        // Load from the default kubeconfig on the machine.
        var config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
        // Load from a specific file:
        //var config = KubernetesClientConfiguration.BuildConfigFromConfigFile(Environment.GetEnvironmentVariable("KUBECONFIG"));

        // Load from in-cluster configuration:
        //var config = KubernetesClientConfiguration.InClusterConfig()

        // Use the config object to create a client.
        var client = new Kubernetes(config);

        var namespaceList = client.ListNamespace();
        
        var podList = client.ListNamespacedPod("license-preproduction");

        var deploymentList = client.ListNamespacedDeployment("license-preproduction");
        
        Assert.NotNull(podList);
    }
}