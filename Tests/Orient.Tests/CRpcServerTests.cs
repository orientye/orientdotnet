using System.Net;
using Orient.Runtime;
using Orient.Rpc;
using Orient.Rpc.Server;

namespace Orient.Tests;

public class CRpcServerTests : OrientTestBase
{
    [Fact]
    public void StartAsyncRejectsInvalidPortBeforeBind()
    {
        var executor = new OrientExecutor();
        var server = new CRpcServer(executor, new CRpcServerOptions
        {
            Address = IPAddress.Loopback,
            Port = 65536,
        });

        OrientExecutorRunner.RunUntilComplete(executor, () =>
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                OrientTask _ = server.StartAsync();
            });
            Assert.Equal(nameof(CRpcServerOptions.Port), ex.ParamName);
            Assert.False(server.IsRunning);
            return OrientTask.CompletedTask(executor);
        });
    }

    [Fact]
    public void StartAsyncThrowsWhenNoOrientExecutorIsBound()
    {
        var executor = new OrientExecutor();
        var server = new CRpcServer(executor);

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            OrientTask _ = server.StartAsync();
        });

        Assert.Contains("OrientExecutor", exception.Message);
    }

    [Fact]
    public void StartStopRunsOnOwnerExecutor()
    {
        var executor = new OrientExecutor();
        var server = new CRpcServer(executor, new CRpcServerOptions
        {
            Address = IPAddress.Loopback,
            Port = 0
        });

        OrientExecutorRunner.RunUntilComplete(executor, async () =>
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
        var executor = new OrientExecutor();
        var server = new CRpcServer(executor, new CRpcServerOptions
        {
            Address = IPAddress.Loopback,
            Port = 0
        });

        OrientExecutorRunner.RunUntilComplete(executor, async () =>
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
        var executor = new OrientExecutor();
        var service = new RecordingService(serviceId);
        var server = new CRpcServer(executor, new CRpcServerOptions
        {
            Address = IPAddress.Loopback,
            Port = 0
        });

        OrientExecutorRunner.RunUntilComplete(executor, async () =>
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
        var executor = new OrientExecutor();
        var server = new CRpcServer(executor, new CRpcServerOptions
        {
            Address = IPAddress.Loopback,
            Port = 0
        });

        OrientExecutorRunner.RunUntilComplete(executor, async () =>
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
            return OrientTask.FromResult((0, Array.Empty<byte>()), OrientExecutor.Current);
        }
    }
}
