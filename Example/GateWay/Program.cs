using CRpc.Async;
using CRpc.Rpc.CRpc.Client;
using CRpc.Rpc.CRpc.Server;

namespace GateWay;

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("GateWay starting...");

        var loop = new CRpcLoop();
        var router = new GateWayRouter();
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var server = new CRpcServer(loop, new CRpcServerOptions
        {
            Port = 7000,
            HandlerFactory = (srv) => new GateWayServerHandler(srv, fallbackServiceId: 0)
        });

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            // Connect to backend services
            var backendClient = new CRpcClient(loop);
            await backendClient.ConnectAsync("127.0.0.1", 7999);
            router.Register(1000, backendClient);
            Console.WriteLine("GateWay connected to backend GreeterService at 127.0.0.1:7999");

            // Register the Gateway forwarding service
            loop.RegisterService(new GateWayServiceImpl(router));

            // Start the CRpc server - use GateWayServerHandler for fallback routing
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
                await server.StopAsync();
            });
        }

        Console.WriteLine("GateWay stopped.");
    }
}
