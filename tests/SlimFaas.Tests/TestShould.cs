using k8s;
using k8s.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace SlimFaas.Tests;
/*
public class TestShould
{

    string ExtractName(string generalName)
    {
        var names = generalName.Split("-");
        if (names.Length <= 0)
        {
            return string.Empty;
        }
        var realName = names[0];
        for (int i = 1; i < names.Length-3; i++)
        {
            realName += $"-{names[i]}";
        }
        return realName;
    }
    
    [Fact]
    public async Task SyncLastTicksBetweenDatabaseAndMemory()
    {

        var generalName = "fibonacci1-youhou-85dfbdd89c-qqxjq";
        var name = ExtractName(generalName);


        var inMemorySettings = new Dictionary<string, string> {
            {"UseKubeConfig", "true"},
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
        //var kubernetesService = new KubernetesService(configuration);
        //var functions = await kubernetesService.ListFunctionsAsync("lightfaas-demo");

        // Load from the default kubeconfig on the machine.
        var config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
        using var client = new Kubernetes(config);
        var namespaces = client.CoreV1.ListNamespace();
        foreach (var ns in namespaces.Items) {
            Console.WriteLine(ns.Metadata.Name);

            
            var list = client.CoreV1.ListNamespacedPod(ns.Metadata.Name);
            MapPodInformations(list);
        }
    }

    private static IList<PodInformation> MapPodInformations(V1PodList list)
    {
        IList<PodInformation> podInformations = new List<PodInformation>();
        foreach (var item in list.Items)
        {
            var containerStatus = item.Status.ContainerStatuses.FirstOrDefault();
            if (containerStatus == null)
            {
                continue;
            }

            var ready = containerStatus.Ready;
            var started = containerStatus.Started;
            var podIP = item.Status.PodIP;
            var podName = item.Metadata.Name;
            var deploymentName = string.Empty;
            if (item.Metadata.Labels.TryGetValue("app", out var label))
            {
                deploymentName = label;
            }

            var podInformation = new PodInformation()
            {
                Started = started,
                Ready = ready,
                Ip = podIP,
                Name = podName,
                DeploymentName = deploymentName
            };

            podInformations.Add(podInformation);
        }

        return podInformations;
    }
}*/