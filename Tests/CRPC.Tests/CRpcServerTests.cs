using System.Net;
using Orient.Runtime;
using Orient.Rpc;
using Orient.Rpc.Server;

namespace CRPC.Tests;

public class CRpcServerTests : CrpcTestBase
{
    [Fact]
    public void StartAsyncRejectsInvalidPortBeforeBind()
    {
        var loop = new OrientLoop();
        var server = new CRpcServer(loop, new CRpcServerOptions
        {
            Address = IPAddress.Loopback,
            Port = 65536,
        });

        OrientLoopRunner.RunUntilComplete(loop, () =>
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                OrientTask _ = server.StartAsync();
            });
            Assert.Equal(nameof(CRpcServerOptions.Port), ex.ParamName);
            Assert.False(server.IsRunning);
            return OrientTask.CompletedTask(loop);
        });
    }

    [Fact]
    public void StartAsyncThrowsWhenNoOrientLoopIsBound()
    {
        var loop = new OrientLoop();
        var server = new CRpcServer(loop);

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            OrientTask _ = server.StartAsync();
        });

        Assert.Contains("OrientLoop", exception.Message);
    }

    [Fact]
    public void StartStopRunsOnOwnerLoop()
    {
        var loop = new OrientLoop();
        var server = new CRpcServer(loop, new CRpcServerOptions
        {
            Address = IPAddress.Loopback,
            Port = 0
        });

        OrientLoopRunner.RunUntilComplete(loop, async () =>
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
        var loop = new OrientLoop();
        var server = new CRpcServer(loop, new CRpcServerOptions
        {
            Address = IPAddress.Loopback,
            Port = 0
        });

        OrientLoopRunner.RunUntilComplete(loop, async () =>
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
        var loop = new OrientLoop();
        var service = new RecordingService(serviceId);
        var server = new CRpcServer(loop, new CRpcServerOptions
        {
            Address = IPAddress.Loopback,
            Port = 0
        });

        OrientLoopRunner.RunUntilComplete(loop, async () =>
        {
            server.Services.Register(service);
            await server.StartAsync();
            await server.StopAsync();

            Assert.True(server.Services.TryGet(serviceId, out var found));
            Assert.Same(service, found);
        });
    }

    [Fact]
    public void StopThenStartAllowsTransportRestart()
    {
        var loop = new OrientLoop();
        var server = new CRpcServer(loop, new CRpcServerOptions
        {
            Address = IPAddress.Loopback,
            Port = 0
        });

        OrientLoopRunner.RunUntilComplete(loop, async () =>
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

        public OrientTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
        {
            return OrientTask.FromResult((0, Array.Empty<byte>()), OrientLoop.Current);
        }
    }
}
