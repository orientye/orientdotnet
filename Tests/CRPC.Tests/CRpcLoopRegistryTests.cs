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

    [Fact]
    public void UnregisterServiceDoesNotRemoveReplacementForSameServiceId()
    {
        const ushort serviceId = 1001;
        var loop = new CRpcLoop();
        var oldService = new RecordingService(serviceId);
        var newService = new RecordingService(serviceId);
        loop.Post(() =>
        {
            loop.RegisterService(oldService);
            loop.RegisterService(newService);
            loop.UnregisterService(oldService);
            Assert.True(loop.TryGetService(serviceId, out var found));
            Assert.Same(newService, found);
        });
        loop.Tick();
    }

    [Fact]
    public void DifferentLoopsCanRegisterSameServiceIdWithoutCollision()
    {
        const ushort serviceId = 1002;
        var firstLoop = new CRpcLoop();
        var secondLoop = new CRpcLoop();
        var firstService = new RecordingService(serviceId);
        var secondService = new RecordingService(serviceId);
        firstLoop.Post(() => firstLoop.RegisterService(firstService));
        secondLoop.Post(() => secondLoop.RegisterService(secondService));
        firstLoop.Tick();
        secondLoop.Tick();

        firstLoop.Post(() =>
        {
            Assert.True(firstLoop.TryGetService(serviceId, out var found));
            Assert.Same(firstService, found);
        });
        secondLoop.Post(() =>
        {
            Assert.True(secondLoop.TryGetService(serviceId, out var found));
            Assert.Same(secondService, found);
        });
        firstLoop.Tick();
        secondLoop.Tick();
    }

    [Fact]
    public void ClearRegisteredServicesRemovesAllServices()
    {
        var loop = new CRpcLoop();
        var service = new RecordingService(1003);
        loop.Post(() =>
        {
            loop.RegisterService(service);
            loop.ClearRegisteredServices();
            Assert.False(loop.TryGetService(service.GetServiceId(), out _));
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
