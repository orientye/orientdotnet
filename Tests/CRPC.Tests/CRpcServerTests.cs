using System.Net;
using CRpc.Async;
using CRpc.Rpc.CRpc.Server;

namespace CRPC.Tests;

public class CRpcServerTests
{
    [Fact]
    public async Task CloseStopsRunningServer()
    {
        var loop = new CRpcLoop();
        var server = new CRpcServer(loop);
        var runTask = server.RunAsync(IPAddress.Loopback, port: 0, registerConsoleCancelHandler: false);

        await WaitUntilAsync(() => server.IsRunning, TimeSpan.FromSeconds(2));

        server.Close();

        var completedTask = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(runTask, completedTask);
        await runTask;
        Assert.False(server.IsRunning);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
