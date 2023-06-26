namespace SlimFaas.Tests;

public class HistoryHttpMemoryServiceTests
{
    [Fact]
    public void GetTicksLastCall_DefaultsToZero()
    {
        var historyHttpMemoryService = new HistoryHttpMemoryService();
        Assert.Equal(0L, historyHttpMemoryService.GetTicksLastCall("test"));
    }
    
    [Fact]
    public void GetTicksLastCall_RetrievesCachedValue()
    {
        var historyHttpMemoryService = new HistoryHttpMemoryService();
        historyHttpMemoryService.SetTickLastCall("test", 1L);
        Assert.Equal(1L, historyHttpMemoryService.GetTicksLastCall("test"));
    }
    
    [Fact]
    public void GetTicksLastCall_ConcurrentWrites()
    {
        var historyHttpMemoryService = new HistoryHttpMemoryService();
        Parallel.For(0, 1000, i =>
        {
            historyHttpMemoryService.SetTickLastCall("test", i);
        });
        Assert.True(historyHttpMemoryService.GetTicksLastCall("test") < 1000);
        Assert.True(historyHttpMemoryService.GetTicksLastCall("test") >= 0);
    }
}