using CRpc.Async;
using CRpc.Rpc.CRpc.Server;

namespace GateWay;

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("GateWay starting...");

        var loop = new CRpcLoop();
        var options = new GateWayOptions();
        var pushRelay = new GateWayPushRelay();
        var sessionTable = new GateWaySessionTable(
            new DefaultBackendClientFactory(),
            new TcpBackendConnector(),
            pushRelay);
        var router = new GateWayRouter(loop, options, sessionTable);
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var server = new CRpcServer(loop, new CRpcServerOptions
        {
            Port = 7000,
            HandlerFactory = srv => new GateWayServerHandler(srv, sessionTable, options.FallbackServiceId),
        });

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            loop.RegisterService(new GateWayServiceImpl(router, options));
            await server.StartAsync(cts.Token);
        });

        Console.WriteLine("GateWay listening on 7000");

        try
        {
            CRpcLoopHost.RunUntilCancelled(loop, cts.Token);
        }
        finally
        {
            CRpcLoopRunner.RunUntilComplete(loop, async () =>
            {
                await sessionTable.DisposeAllAsync();
                await server.StopAsync();
            });
        }

        Console.WriteLine("GateWay stopped.");
    }
}
