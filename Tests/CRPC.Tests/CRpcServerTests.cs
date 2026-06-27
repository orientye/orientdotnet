using System.Net;
using CRpc.Async;
using CRpc.Rpc;
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

    [Fact]
    public void StartAsyncThrowsWhenAlreadyStarted()
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

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await server.StartAsync();
            });

            Assert.Contains("already started", exception.Message, StringComparison.OrdinalIgnoreCase);
            await server.StopAsync();
        });
    }

    [Fact]
    public void StopAsyncPreservesRegisteredServices()
    {
        const ushort serviceId = 4201;
        var loop = new CRpcLoop();
        var service = new RecordingService(serviceId);
        var server = new CRpcServer(loop, new CRpcServerOptions
        {
            Address = IPAddress.Loopback,
            Port = 0
        });

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            loop.RegisterService(service);
            await server.StartAsync();
            await server.StopAsync();

            Assert.True(loop.TryGetService(serviceId, out var found));
            Assert.Same(service, found);
        });
    }

    [Fact]
    public void StopThenStartAllowsTransportRestart()
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
            await server.StopAsync();
            Assert.False(server.IsRunning);

            await server.StartAsync();
            Assert.True(server.IsRunning);

            await server.StopAsync();
        });
    }

    private sealed class RecordingService : IRpcService
    {
        public RecordingService(ushort serviceId)
        {
            ServiceId = serviceId;
        }

        public ushort ServiceId { get; }

        public ushort GetServiceId() => ServiceId;

        public CRpcTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
        {
            return CRpcTask.FromResult((0, Array.Empty<byte>()), CRpcLoop.Current);
        }
    }
}
