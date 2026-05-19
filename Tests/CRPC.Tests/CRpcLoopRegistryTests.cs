using CRpc.Async;
using CRpc.Rpc;

namespace CRPC.Tests;

public class CRpcLoopRegistryTests
{
    [Fact]
    public void RegisterServiceRequiresLoopThread()
    {
        var loop = new CRpcLoop();
        var service = new RecordingService(1000);

        Assert.Throws<InvalidOperationException>(() => loop.RegisterService(service));
    }

    [Fact]
    public void RegisterAndTryGetServiceOnLoopThread()
    {
        var loop = new CRpcLoop();
        var service = new RecordingService(1000);
        loop.Post(() => loop.RegisterService(service));
        loop.Tick();
        loop.Post(() =>
        {
            Assert.True(loop.TryGetService(1000, out var found));
            Assert.Same(service, found);
        });
        loop.Tick();
    }

    [Fact]
    public void UnregisterServiceRemovesService()
    {
        var loop = new CRpcLoop();
        var service = new RecordingService(1000);
        loop.Post(() =>
        {
            loop.RegisterService(service);
            loop.UnregisterService(service);
            Assert.False(loop.TryGetService(1000, out _));
        });
        loop.Tick();
    }

    private sealed class RecordingService : IRpcService
    {
        private readonly ushort serviceId;

        public RecordingService(ushort serviceId)
        {
            this.serviceId = serviceId;
        }

        public ushort GetServiceId() => serviceId;

        public CRpcTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req) =>
            CRpcTask.FromResult((0, Array.Empty<byte>()));
    }
}
