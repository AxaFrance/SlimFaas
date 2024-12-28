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
                if (attempt <= 0)
                {
                    return await action();
                }

                var delay = delays[attempt];
                logger.LogWarning("Try {Attempt} : wait number {Delay} second", attempt, delay);
                await Task.Delay((int)delay * 1000);

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
                    await Task.Delay(delay * 1000);
                }

                await action();
                return;
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        throw new AggregateException(exceptions);
    }

    public static async Task<HttpResponseMessage> DoRequestAsync(
        Func<Task<HttpResponseMessage>> action,
        ILogger logger,
        IList<int> delays,
        IList<int> httpStatusRetries
    )
    {
        var exceptions = new List<Exception>();

        for (int attempt = 0; attempt < delays.Count; attempt++)
        {
            try
            {
                if (attempt <= 0)
                {
                    return await action();
                }

                var delay = delays[attempt];
                logger.LogWarning("Try {Attempt} : wait number {Delay} second", attempt, delay);
                await Task.Delay(delay * 1000);

                var responseMessage = await action();
                var statusCode = (int)responseMessage.StatusCode;
                if (!httpStatusRetries.Contains(statusCode))
                {
                    return responseMessage;
                }
                responseMessage.Dispose();
                exceptions.Add(new Exception($"Received code Http {statusCode}"));
            }
            catch (HttpRequestException ex)
            {
                exceptions.Add(ex);
            }
        }

        throw new AggregateException(exceptions);
    }
}
