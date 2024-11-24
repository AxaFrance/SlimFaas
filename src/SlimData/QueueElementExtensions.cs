namespace SlimData;

public static class QueueElementExtensions
{
    public static int[] HttpStatusCodesWorthRetrying =
    [
        408 , // HttpStatusCode.RequestTimeout,
        500, // HttpStatusCode.InternalServerError, 
        502, // HttpStatusCode.BadGateway, 
        503, // HttpStatusCode.ServiceUnavailable,
        504, // HttpStatusCode.GatewayTimeout 
    ];
    
    public static bool IsTimeout(this QueueElement element, long nowTicks, int timeout=30)
    {
        if (element.RetryQueueElements.Count <= 0) return false;
        var retryQueueElement = element.RetryQueueElements[^1];
        if (retryQueueElement.EndTimeStamp == 0 &&
            retryQueueElement.StartTimeStamp + TimeSpan.FromSeconds(timeout).Ticks <= nowTicks)
        {
            return true;
        }
        return false;   
    }
    
    public static bool IsWaitingForRetry(this QueueElement element, long nowTicks, List<int> retries, int timeoutSeconds=30)
    {
        var count = element.RetryQueueElements.Count;
        if(count == 0 || count > retries.Count ) return false;
        
        if(element.IsFinished(nowTicks, retries, timeoutSeconds)) return false;

        if(element.IsRunning(nowTicks, timeoutSeconds)) return false;
        
        var retryQueueElement = element.RetryQueueElements[^1];
        var retryTimeout = retries[count - 1];
        if(element.IsTimeout(nowTicks, timeoutSeconds) && retryQueueElement.StartTimeStamp + TimeSpan.FromSeconds(retryTimeout).Ticks <= nowTicks)
        {
            return true;
        }

        if (retryQueueElement.EndTimeStamp != 0 &&
            (retryQueueElement.EndTimeStamp + TimeSpan.FromSeconds(retryTimeout).Ticks > nowTicks))
        {
            return true;
        }
        
        return false;
    }
    
    public static bool IsFinished(this QueueElement queueElement, long nowTicks,  List<int> retries, int timeoutSeconds=30)
    {
        var count = queueElement.RetryQueueElements.Count;
        if (count <= 0) return false;
        var retryQueueElement = queueElement.RetryQueueElements[^1];
        if (retryQueueElement.EndTimeStamp > 0 &&
            !HttpStatusCodesWorthRetrying.Contains(retryQueueElement.HttpCode))
        {
            return true;
        }

        if (retries.Count < count)
        {
            if (queueElement.IsTimeout(nowTicks, timeoutSeconds) || retryQueueElement.EndTimeStamp > 0)
            {
                return true;
            }
        }
        
        return false;
    }
    
    public static bool IsRunning(this QueueElement queueElement, long nowTicks, int timeoutSeconds=30)
    {
        if (queueElement.RetryQueueElements.Count <= 0) return false;
        var retryQueueElement = queueElement.RetryQueueElements[^1];
        if (retryQueueElement.EndTimeStamp == 0 &&
            !queueElement.IsTimeout(nowTicks, timeoutSeconds))
        {
            return true;
        }
        return false;
    }
    
    public static List<QueueElement> GetQueueTimeoutElement(this List<QueueElement> element, long nowTicks, int timeout=30)
    {
        var timeoutElements = new List<QueueElement>();
        foreach (var queueElement in element)
        {
            if(queueElement.IsTimeout(nowTicks, timeout))
            {
                timeoutElements.Add(queueElement);
            }
           
        }
        return timeoutElements;
    }
    
    public static List<QueueElement> GetQueueRunningElement(this List<QueueElement> element, long nowTicks, int timeoutSeconds=30)
    {
        var runningElement = new List<QueueElement>();
        foreach (var queueElement in element)
        {
            if(queueElement.IsRunning(nowTicks, timeoutSeconds))
            {
                runningElement.Add(queueElement);
            }
        }
        return runningElement;
    }
    
    public static List<QueueElement> GetQueueWaitingForRetryElement(this List<QueueElement> element, long nowTicks, List<int> retries, int timeoutSeconds=30)
    {
        var waitingForRetry = new List<QueueElement>();
        foreach (var queueElement in element)
        {
            if (queueElement.IsWaitingForRetry(nowTicks, retries, timeoutSeconds))
            {
                waitingForRetry.Add(queueElement);
            }
        }
        return waitingForRetry;
    }
    
    public static List<QueueElement> GetQueueAvailableElement(this List<QueueElement> elements, List<int> retries, long nowTicks, int maximum, int timeoutSeconds=30)
    {
        var runningElements = elements.GetQueueRunningElement(nowTicks);
        var runningWaitingForRetryElements = elements.GetQueueWaitingForRetryElement(nowTicks, retries);
        var finishedElements = elements.GetQueueFinishedElement(nowTicks, retries, timeoutSeconds);
        var availableElements = new List<QueueElement>();
        var currentCount = runningElements.Count + runningWaitingForRetryElements.Count;
        var currentElements = elements.Except(runningElements).Except(runningWaitingForRetryElements).Except(finishedElements);
        
        if (currentCount >= maximum)
        {
            return availableElements;
        }
       
        foreach (var queueElement in currentElements)
        {
            if (currentCount == maximum)
            {
                return availableElements;
            }
            availableElements.Add(queueElement);
            currentCount++;
        }
        return availableElements;
    }
    
    public static IList<QueueElement> GetQueueFinishedElement(this IList<QueueElement?> element, long nowTicks, List<int> retries, int timeoutSeconds=30)
    {
        var queueFinishedElements = new List<QueueElement>();
        foreach (var queueElement in element)
        {
            if (queueElement.IsFinished(nowTicks, retries, timeoutSeconds))
            {
                queueFinishedElements.Add(queueElement);
            }
           
        }
        return queueFinishedElements;
    }

}