using RaftNode;
using SlimData.Commands;

namespace SlimData.Tests;

public class CommandsTests
{
    private protected static byte[] RandomBytes(int size)
    {
        var result = new byte[size];
        Random.Shared.NextBytes(result);
        return result;
    }

    [Fact]
    public static async Task InterpreterWithPersistentState()
    {
        byte[] bytes = RandomBytes(1000);
        using var wal = new SlimPersistentState(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        var entry1 = wal.CreateLogEntry(new ListLeftPushCommand { Key  = "youhou" , Value  = bytes });
        await wal.AppendAsync(entry1);
        //Assert.Equal(0, wal.SlimDataState.queuesBin.);
        await wal.CommitAsync(CancellationToken.None);
        Assert.Equal(bytes, wal.SlimDataState.queues["youhou"].First().ToArray());
    }


}
