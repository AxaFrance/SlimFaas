namespace SlimData;

public static class QueueElementExtensions
{
    public static int[] httpStatusCodesWorthRetrying =
    {
        408 , // HttpStatusCode.RequestTimeout, // 408
        500, // HttpStatusCode.InternalServerError, // 500
        502, // HttpStatusCode.BadGateway, // 502
        503, //HttpStatusCode.ServiceUnavailable, // 503
        504, // HttpStatusCode.GatewayTimeout // 504
    };
    
    public static List<QueueElement> GetQueueTimeoutElement(this List<QueueElement> element, long nowTicks, int timeout=30)
    {
        var timeoutElements = new List<QueueElement>();
        foreach (var queueElement in element)
        {
            if(queueElement.RetryQueueElements.Count > 0)
            {
                var retryQueueElement = queueElement.RetryQueueElements[^1];
                if (retryQueueElement.EndTimeStamp == 0 &&
                    retryQueueElement.StartTimeStamp + TimeSpan.FromSeconds(timeout).Ticks <= nowTicks)
                {
                    timeoutElements.Add(queueElement);
                }
            }
           
        }
        return timeoutElements;
    }
    
    public static List<QueueElement> GetQueueRunningElement(this List<QueueElement> element, long nowTicks, int timeout=30)
    {
        var runningElement = new List<QueueElement>();
        foreach (var queueElement in element)
        {
            if(queueElement.RetryQueueElements.Count > 0)
            {
                var retryQueueElement = queueElement.RetryQueueElements[^1];
                if (retryQueueElement.EndTimeStamp == 0 &&
                    retryQueueElement.StartTimeStamp + TimeSpan.FromSeconds(timeout).Ticks > nowTicks)
                {
                    runningElement.Add(queueElement);
                }
            }
           
        }
        return runningElement;
    }
    
    public static List<QueueElement> GetQueueAvailableElement(this List<QueueElement> element, List<int> retries, long nowTicks, int maximum)
    {
        var currentCount = 0;
        var availableElements = new List<QueueElement>();
        foreach (var queueElement in element)
        {
            if (currentCount == maximum)
            {
                return availableElements;
            }
            var count = queueElement.RetryQueueElements.Count;
            if (count == 0)
            {
                availableElements.Add(queueElement);
                currentCount++;
            }
            else
            {
                var retryQueueElement = queueElement.RetryQueueElements[^1];
                if (httpStatusCodesWorthRetrying.Contains( retryQueueElement.HttpCode)
                    && retries.Count <= count 
                    && retryQueueElement.EndTimeStamp != 0 
                    && nowTicks > retryQueueElement.EndTimeStamp + TimeSpan.FromSeconds(retries[count - 1]).Ticks 
                   )
                {
                    availableElements.Add(queueElement);
                    currentCount++;
                }
            }
           
        }
        return availableElements;
    }
    
    public static IList<QueueElement> GetQueueFinishedElement(this IList<QueueElement?> element, List<int> retries)
    {
        var queueFinishedElement = new List<QueueElement>();
        foreach (var queueElement in element)
        {
            var count = queueElement.RetryQueueElements.Count;
            if(count > 0)
            {
                var retryQueueElement = queueElement.RetryQueueElements[^1];
                if (retryQueueElement.HttpCode is >= 0 and < 400 || retries.Count <= count)
                {
                    queueFinishedElement.Add(queueElement);
                }
            }
           
        }
        return queueFinishedElement;
    }

}