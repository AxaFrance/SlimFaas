namespace SlimFaas;

public static class Retry
{

    public static async Task<T> DoAsync<T>(
        Func<Task<T>> action,
        ILogger logger,
        IList<int> delays
    )
    {
        var exceptions = new List<Exception>();

        for (int attempt = 0; attempt < delays.Count; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var delay = delays[attempt];
                    logger.LogWarning("Try {Attempt} : wait numnber {Delay} second", attempt, delay);
                    await Task.Delay((int)delay * 1000);
                }

                return await action();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        throw new AggregateException(exceptions);
    }

    public static async Task DoAsync(
        Func<Task> action,
        ILogger logger,
        IList<int> delays
    )
    {
        var exceptions = new List<Exception>();

        for (int attempt = 0; attempt < delays.Count; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var delay = delays[attempt];
                    logger.LogWarning("Try {Attempt} : wait numnber {Delay} second", attempt, delay);
                    await Task.Delay((int)delay * 1000);
                }

                // Exécuter la méthode asynchrone
                await action();
                return; // Si succès, on sort de la fonction
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        // Si toutes les tentatives échouent, lever une AggregateException
        throw new AggregateException(exceptions);
    }
}
