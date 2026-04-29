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
        var service = new TestService(NextServiceId());
        var server = new CRpcServer();
        server.RegisterService(service);

        server.UnregisterService(service);

        Assert.False(CRpcServer.TryGetService(service.GetServiceId(), out _));
    }

    [Fact]
    public void UnregisterServiceDoesNotRemoveReplacementForSameServiceId()
    {
        var serviceId = NextServiceId();
        var oldService = new TestService(serviceId);
        var newService = new TestService(serviceId);
        var server = new CRpcServer();
        server.RegisterService(oldService);
        server.RegisterService(newService);

        server.UnregisterService(oldService);

        Assert.True(CRpcServer.TryGetService(serviceId, out var registeredService));
        Assert.Same(newService, registeredService);
    }

    [Fact]
    public async Task CloseStopsRunningServer()
    {
        var server = new CRpcServer();
        var runTask = server.RunAsync(IPAddress.Loopback, port: 0, registerConsoleCancelHandler: false);

        await WaitUntilAsync(() => server.IsRunning, TimeSpan.FromSeconds(2));

        server.Close();

        var completedTask = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(runTask, completedTask);
        await runTask;
        Assert.False(server.IsRunning);
    }

    private static int NextServiceId()
    {
        return Interlocked.Increment(ref nextServiceId);
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

    private sealed class TestService : IRpcService
    {
        private readonly int serviceId;

        public TestService(int serviceId)
        {
            this.serviceId = serviceId;
        }

        public int GetServiceId()
        {
            return serviceId;
        }

        public CRpcTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
        {
            return CRpcTask.FromResult((0, Array.Empty<byte>()), CRpcLoop.Current);
        }
    }
}
