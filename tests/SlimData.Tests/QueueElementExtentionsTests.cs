namespace SlimData.Tests;

public class QueueElementExtentionsTests
{
    [Fact]
    public static void QueueElementExtensionsGetQueueRunningElement()
    {
        // I want a test which text my extention
        var nowTicks = DateTime.UtcNow.Ticks;

        var timeout = 30;
        var timeoutSpanTicks = TimeSpan.FromSeconds(31).Ticks;
        List<QueueElement> queueElements = new();
        queueElements.Add(new QueueElement(new ReadOnlyMemory<byte>([1]), "-1", 090902, timeout, SlimDataInterpreter.Retries, new List<QueueHttpTryElement>()
        {
            new(nowTicks -100, nowTicks, 500),
            new(nowTicks -50, nowTicks, 500),
            new(nowTicks -20, nowTicks, 500),
            new(nowTicks -10, nowTicks, 500),
        }));
        queueElements.Add(new QueueElement(new ReadOnlyMemory<byte>([1]), "0", 090902, timeout, SlimDataInterpreter.Retries, new List<QueueHttpTryElement>()
        {
            new(nowTicks - timeoutSpanTicks -100, nowTicks, 500),
            new(nowTicks- timeoutSpanTicks -50, nowTicks, 500),
            new(nowTicks- timeoutSpanTicks -30, nowTicks, 500),
            new(nowTicks- timeoutSpanTicks -20, 0, 0),
        }));
        queueElements.Add(new QueueElement(new ReadOnlyMemory<byte>([1]), "0-ok", 090902, timeout, SlimDataInterpreter.Retries, new List<QueueHttpTryElement>()
        {
            new(nowTicks  -100, nowTicks, 200),
        }));
        queueElements.Add(new QueueElement(new ReadOnlyMemory<byte>([1]), "1", 090902, timeout, SlimDataInterpreter.Retries, new List<QueueHttpTryElement>()
        {
            new(nowTicks - 1000, nowTicks, 500),
            new(nowTicks- 500, nowTicks, 500),
            new(nowTicks- 200, nowTicks, 500),
            new(nowTicks- 100, 0, 0),
        }));
        queueElements.Add(new QueueElement(new ReadOnlyMemory<byte>([1]), "1timeout", 090902, timeout, SlimDataInterpreter.Retries, new List<QueueHttpTryElement>()
        {
            new(nowTicks - 1000, nowTicks, 500),
            new(nowTicks- 500, nowTicks, 500),
            new(nowTicks- 400, nowTicks, 500),
            new(nowTicks- timeoutSpanTicks, 0, 0),
        }));
        queueElements.Add(new QueueElement(new ReadOnlyMemory<byte>([1]), "2", 090902, timeout, SlimDataInterpreter.Retries, new List<QueueHttpTryElement>()));
        queueElements.Add(new QueueElement(new ReadOnlyMemory<byte>([1]), "3", 090902, timeout, SlimDataInterpreter.Retries, new List<QueueHttpTryElement>()));
        queueElements.Add(new QueueElement(new ReadOnlyMemory<byte>([1]), "4", 090902, timeout, SlimDataInterpreter.Retries, new List<QueueHttpTryElement>()));

        var availableElements = queueElements.GetQueueAvailableElement(nowTicks, 3);

        Assert.Equal(2, availableElements.Count);
        Assert.Equal("2", availableElements[0].Id);
        Assert.Equal("3", availableElements[1].Id);

        var runningElements = queueElements.GetQueueRunningElement(nowTicks);
        Assert.Equal(1, runningElements.Count);
        Assert.Equal("1", runningElements[0].Id);


        var finishedElements = queueElements.GetQueueFinishedElement(nowTicks);
        Assert.Equal(4, finishedElements.Count);
    }

   /*  [Fact]
    public static void QueueElementExtensionsGetQueueRunningElement2()
    {
        // I want a test which text my extention
        var nowTicks = DateTime.UtcNow.Ticks;
        List<QueueElement> queueElements = new();
        queueElements.Add(new QueueElement(new ReadOnlyMemory<byte>([1]), "1", nowTicks, new List<QueueHttpTryElement>()));
        queueElements.Add(new QueueElement(new ReadOnlyMemory<byte>([1]), "2", nowTicks, new List<QueueHttpTryElement>()));
        queueElements.Add(new QueueElement(new ReadOnlyMemory<byte>([1]), "3", nowTicks, new List<QueueHttpTryElement>()));

        var availableElements = queueElements.GetQueueAvailableElement(SlimDataInterpreter.Retries, nowTicks, 1, 30);

        foreach (QueueElement queueElement in queueElements)
        {
            Assert.Equal(2, availableElements.Count);
            Assert.Equal("2", availableElements[0].Id);
            Assert.Equal("3", availableElements[1].Id);
        }

        var runningElements = queueElements.GetQueueRunningElement(nowTicks);
        Assert.Equal(1, runningElements.Count);
        Assert.Equal("1", runningElements[0].Id);


        var finishedElements = queueElements.GetQueueFinishedElement(nowTicks, SlimDataInterpreter.Retries);
        Assert.Equal(4, finishedElements.Count);
    }*/
}
