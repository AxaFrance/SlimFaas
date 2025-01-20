namespace SlimFaas;

public interface IJobService
{
   Task CreateJobAsync(string jobName);
}

public class JobService(IKubernetesService kubernetesService) : IJobService
{
    private readonly string _namespace = Environment.GetEnvironmentVariable(EnvironmentVariables.Namespace) ??
                                         EnvironmentVariables.NamespaceDefault;

    public async Task CreateJobAsync(string jobName)
    {
        await kubernetesService.CreateJobAsync(jobName, _namespace);
    }


}
