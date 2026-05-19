using System.Net;
using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Server;

namespace CRPC.Tests;

public class CRpcServerTests
{
    private static int nextServiceId = 2000;

    [Fact]
    public void UnregisterServiceRemovesRegisteredService()
    {
        var loop = new CRpcLoop();
        var service = new TestService(NextServiceId());
        var server = new CRpcServer(loop);
        RegisterOnLoop(loop, server, service);

        UnregisterOnLoop(loop, server, service);

        var observed = TryGetRegisteredServiceOnLoop(loop, server, service.GetServiceId());
        Assert.False(observed.Found);
    }

    [Fact]
    public void UnregisterServiceDoesNotRemoveReplacementForSameServiceId()
    {
        var serviceId = NextServiceId();
        var loop = new CRpcLoop();
        var oldService = new TestService(serviceId);
        var newService = new TestService(serviceId);
        var server = new CRpcServer(loop);
        RegisterOnLoop(loop, server, oldService);
        RegisterOnLoop(loop, server, newService);

        UnregisterOnLoop(loop, server, oldService);

        var observed = TryGetRegisteredServiceOnLoop(loop, server, serviceId);
        Assert.True(observed.Found);
        Assert.Same(newService, observed.Service);
    }

    [Fact]
    public void DifferentServersCanRegisterSameServiceIdWithoutCollision()
    {
        var serviceId = NextServiceId();
        var firstLoop = new CRpcLoop();
        var secondLoop = new CRpcLoop();
        var firstService = new TestService(serviceId);
        var secondService = new TestService(serviceId);
        var firstServer = new CRpcServer(firstLoop);
        var secondServer = new CRpcServer(secondLoop);

        RegisterOnLoop(firstLoop, firstServer, firstService);
        RegisterOnLoop(secondLoop, secondServer, secondService);

        var firstObserved = TryGetRegisteredServiceOnLoop(firstLoop, firstServer, serviceId);
        Assert.True(firstObserved.Found);
        Assert.Same(firstService, firstObserved.Service);
        var secondObserved = TryGetRegisteredServiceOnLoop(secondLoop, secondServer, serviceId);
        Assert.True(secondObserved.Found);
        Assert.Same(secondService, secondObserved.Service);
    }

    [Fact]
    public void RegistryOperationsRequireServerLoopThread()
    {
        var loop = new CRpcLoop();
        var service = new TestService(NextServiceId());
        var server = new CRpcServer(loop);

        Assert.Throws<InvalidOperationException>(() => server.RegisterService(service));
        Assert.Throws<InvalidOperationException>(() => server.UnregisterService(service));
        Assert.Throws<InvalidOperationException>(() => server.TryGetRegisteredService(service.GetServiceId(), out _));
    }

    [Fact]
    public void StopCleanupClearsRegisteredServicesOnLoopThread()
    {
        var loop = new CRpcLoop();
        var service = new TestService(NextServiceId());
        var server = new CRpcServer(loop);

        loop.Post(() =>
        {
            server.RegisterService(service);

            server.ClearRegisteredServices();

            Assert.False(server.TryGetRegisteredService(service.GetServiceId(), out _));
        });

        loop.Tick();
    }

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

    private static ushort NextServiceId()
    {
        return checked((ushort)Interlocked.Increment(ref nextServiceId));
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

    private static void RegisterOnLoop(CRpcLoop loop, CRpcServer server, IRpcService service)
    {
        loop.Post(() => server.RegisterService(service));
        loop.Tick();
    }

    private static void UnregisterOnLoop(CRpcLoop loop, CRpcServer server, IRpcService service)
    {
        loop.Post(() => server.UnregisterService(service));
        loop.Tick();
    }

    private static (bool Found, IRpcService? Service) TryGetRegisteredServiceOnLoop(CRpcLoop loop, CRpcServer server, ushort serviceId)
    {
        bool found = false;
        IRpcService? service = null;
        loop.Post(() =>
        {
            found = server.TryGetRegisteredService(serviceId, out var registeredService);
            service = registeredService;
        });
        loop.Tick();
        return (found, service);
    }

    private sealed class TestService : IRpcService
    {
        private readonly ushort serviceId;

        public TestService(int serviceId)
        {
            this.serviceId = checked((ushort)serviceId);
        }

        public ushort GetServiceId()
        {
            return serviceId;
        }

        public CRpcTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
        {
            return CRpcTask.FromResult((0, Array.Empty<byte>()), CRpcLoop.Current);
        }
    }
}
