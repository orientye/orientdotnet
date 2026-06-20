using System.Net;
using CRpc.Async;
using CRpc.Rpc.CRpc.Server;

namespace CRPC.Tests;

public class CRpcServerTests : CrpcTestBase
{
    [Fact]
    public void StartAsyncThrowsWhenNoCRpcLoopIsBound()
    {
        var loop = new CRpcLoop();
        var server = new CRpcServer(loop);

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            CRpcTask _ = server.StartAsync();
        });

        Assert.Contains("CRpcLoop", exception.Message);
    }

    [Fact]
    public void StartStopRunsOnOwnerLoop()
    {
        var loop = new CRpcLoop();
        var server = new CRpcServer(loop, new CRpcServerOptions
        {
            Address = IPAddress.Loopback,
            Port = 0
        });

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            await server.StartAsync();
            Assert.True(server.IsRunning);

            await server.StopAsync();
            Assert.False(server.IsRunning);
        });
    }
}
