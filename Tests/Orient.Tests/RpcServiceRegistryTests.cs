using Orient.Runtime;
using Orient.Rpc;
using Orient.Rpc.Server;

namespace Orient.Tests;

public class RpcServiceRegistryTests : OrientTestBase
{
    [Fact]
    public void RegisterRequiresLoopThread()
    {
        var executor = new OrientExecutor();
        var registry = new RpcServiceRegistry(executor);
        var service = new RecordingService(1000);

        Assert.Throws<InvalidOperationException>(() => { registry.Register(service); });
    }

    [Fact]
    public void RegisterAndTryGetOnLoopThread()
    {
        var executor = new OrientExecutor();
        var registry = new RpcServiceRegistry(executor);
        var service = new RecordingService(1000);
        executor.Post(() => registry.Register(service));
        executor.Tick();
        executor.Post(() =>
        {
            Assert.True(registry.TryGet(1000, out var found));
            Assert.Same(service, found);
        });
        executor.Tick();
    }

    [Fact]
    public void UnregisterRemovesService()
    {
        var executor = new OrientExecutor();
        var registry = new RpcServiceRegistry(executor);
        var service = new RecordingService(1000);
        executor.Post(() =>
        {
            registry.Register(service);
            registry.Unregister(service);
            Assert.False(registry.TryGet(1000, out _));
        });
        executor.Tick();
    }

    [Fact]
    public void UnregisterDoesNotRemoveReplacementForSameServiceId()
    {
        const ushort serviceId = 1001;
        var executor = new OrientExecutor();
        var registry = new RpcServiceRegistry(executor);
        var oldService = new RecordingService(serviceId);
        var newService = new RecordingService(serviceId);
        executor.Post(() =>
        {
            registry.Register(oldService);
            registry.Register(newService);
            registry.Unregister(oldService);
            Assert.True(registry.TryGet(serviceId, out var found));
            Assert.Same(newService, found);
        });
        executor.Tick();
    }

    [Fact]
    public void DifferentRegistriesOnDifferentLoopsDoNotCollide()
    {
        const ushort serviceId = 1002;
        var firstLoop = new OrientExecutor();
        var secondLoop = new OrientExecutor();
        var firstRegistry = new RpcServiceRegistry(firstLoop);
        var secondRegistry = new RpcServiceRegistry(secondLoop);
        var firstService = new RecordingService(serviceId);
        var secondService = new RecordingService(serviceId);

        DedicatedExecutorThread.Run(firstLoop, executor =>
        {
            executor.Post(() => firstRegistry.Register(firstService));
            executor.Tick();
            Assert.True(firstRegistry.TryGet(serviceId, out var found));
            Assert.Same(firstService, found);
        });

        DedicatedExecutorThread.Run(secondLoop, executor =>
        {
            executor.Post(() => secondRegistry.Register(secondService));
            executor.Tick();
            Assert.True(secondRegistry.TryGet(serviceId, out var found));
            Assert.Same(secondService, found);
        });
    }

    [Fact]
    public void ClearRemovesAllServices()
    {
        var executor = new OrientExecutor();
        var registry = new RpcServiceRegistry(executor);
        var service = new RecordingService(1003);
        executor.Post(() =>
        {
            registry.Register(service);
            registry.Clear();
            Assert.False(registry.TryGet(service.GetServiceId(), out _));
        });
        executor.Tick();
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
