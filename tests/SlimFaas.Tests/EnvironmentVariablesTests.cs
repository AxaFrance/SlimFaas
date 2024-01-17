using Microsoft.Extensions.Logging;
using Moq;

namespace SlimFaas.Tests;

public class EnvironmentVariablesTests
{

    [Fact]
    public void ReadBooleanValue()
    {
        Mock<ILogger<EnvironmentVariablesTests>> logger = new Mock<ILogger<EnvironmentVariablesTests>>();
        Environment.SetEnvironmentVariable("MY_ENV", "true");
        Assert.True(EnvironmentVariables.ReadBoolean(logger.Object, "MY_ENV", false));
    }

    [Fact]
    public void FailReadBooleanValue()
    {
        Mock<ILogger<EnvironmentVariablesTests>> logger = new Mock<ILogger<EnvironmentVariablesTests>>();
        Environment.SetEnvironmentVariable("MY_ENV", "wrong");
        Assert.True(EnvironmentVariables.ReadBoolean(logger.Object, "MY_ENV", true));
        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()), Times.AtLeastOnce);
    }

    [Fact]
    public void ReadIntegerValue()
    {
        Mock<ILogger<EnvironmentVariablesTests>> logger = new Mock<ILogger<EnvironmentVariablesTests>>();
        Environment.SetEnvironmentVariable("MY_ENV", "20");
        Assert.Equal(20, EnvironmentVariables.ReadInteger(logger.Object, "MY_ENV", 10));
    }

    [Fact]
    public void FailReadIntegerValue()
    {
        Mock<ILogger<EnvironmentVariablesTests>> logger = new Mock<ILogger<EnvironmentVariablesTests>>();
        Environment.SetEnvironmentVariable("MY_ENV", "wrong");
        Assert.Equal(10, EnvironmentVariables.ReadInteger(logger.Object, "MY_ENV", 10));
        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()), Times.AtLeastOnce);
    }
}
