namespace SlimFaas;

public interface IJobService
{
   Task CreateJobAsync(string jobName);
}

public class JobService : IJobService
{
    private readonly IKubernetesService _kubernetesService;

    public JobService(IKubernetesService kubernetesService)
    {
        _kubernetesService = kubernetesService;
    }

    public async Task CreateJobAsync(string jobName)
    {
        await _kubernetesService.CreateJobAsync(jobName);
    }


}
