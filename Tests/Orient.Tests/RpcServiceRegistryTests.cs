using Orient.Runtime;
using Orient.Rpc;
using Orient.Rpc.Server;

namespace Orient.Tests;

public class RpcServiceRegistryTests : OrientTestBase
{
    [Fact]
    public void RegisterRequiresLoopThread()
    {
        var loop = new OrientLoop();
        var registry = new RpcServiceRegistry(loop);
        var service = new RecordingService(1000);

        Assert.Throws<InvalidOperationException>(() => { registry.Register(service); });
    }

    [Fact]
    public void RegisterAndTryGetOnLoopThread()
    {
        var loop = new OrientLoop();
        var registry = new RpcServiceRegistry(loop);
        var service = new RecordingService(1000);
        loop.Post(() => registry.Register(service));
        loop.Tick();
        loop.Post(() =>
        {
            Assert.True(registry.TryGet(1000, out var found));
            Assert.Same(service, found);
        });
        loop.Tick();
    }

    [Fact]
    public void UnregisterRemovesService()
    {
        var loop = new OrientLoop();
        var registry = new RpcServiceRegistry(loop);
        var service = new RecordingService(1000);
        loop.Post(() =>
        {
            registry.Register(service);
            registry.Unregister(service);
            Assert.False(registry.TryGet(1000, out _));
        });
        loop.Tick();
    }

    [Fact]
    public void UnregisterDoesNotRemoveReplacementForSameServiceId()
    {
        const ushort serviceId = 1001;
        var loop = new OrientLoop();
        var registry = new RpcServiceRegistry(loop);
        var oldService = new RecordingService(serviceId);
        var newService = new RecordingService(serviceId);
        loop.Post(() =>
        {
            registry.Register(oldService);
            registry.Register(newService);
            registry.Unregister(oldService);
            Assert.True(registry.TryGet(serviceId, out var found));
            Assert.Same(newService, found);
        });
        loop.Tick();
    }

    [Fact]
    public void DifferentRegistriesOnDifferentLoopsDoNotCollide()
    {
        const ushort serviceId = 1002;
        var firstLoop = new OrientLoop();
        var secondLoop = new OrientLoop();
        var firstRegistry = new RpcServiceRegistry(firstLoop);
        var secondRegistry = new RpcServiceRegistry(secondLoop);
        var firstService = new RecordingService(serviceId);
        var secondService = new RecordingService(serviceId);

        DedicatedLoopThread.Run(firstLoop, loop =>
        {
            loop.Post(() => firstRegistry.Register(firstService));
            loop.Tick();
            Assert.True(firstRegistry.TryGet(serviceId, out var found));
            Assert.Same(firstService, found);
        });

        DedicatedLoopThread.Run(secondLoop, loop =>
        {
            loop.Post(() => secondRegistry.Register(secondService));
            loop.Tick();
            Assert.True(secondRegistry.TryGet(serviceId, out var found));
            Assert.Same(secondService, found);
        });
    }

    [Fact]
    public void ClearRemovesAllServices()
    {
        var loop = new OrientLoop();
        var registry = new RpcServiceRegistry(loop);
        var service = new RecordingService(1003);
        loop.Post(() =>
        {
            registry.Register(service);
            registry.Clear();
            Assert.False(registry.TryGet(service.GetServiceId(), out _));
        });
        loop.Tick();
    }

    private sealed class RecordingService : IRpcService
    {
        private readonly ushort serviceId;

        public RecordingService(ushort serviceId) => this.serviceId = serviceId;

        public ushort GetServiceId() => serviceId;

        public OrientTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req) =>
            OrientTask.FromResult((0, Array.Empty<byte>()));
    }
}
