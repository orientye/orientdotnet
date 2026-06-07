using CRpc.Async;
using CRpc.Rpc.CRpc.Server;

namespace GateWay;

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("GateWay starting...");

        var configPath = ResolveConfigPath(args);
        var config = GateWayConfigLoader.LoadOrDefault(configPath);
        var poolRegistry = config.BuildRegistry();

        var loop = new CRpcLoop();
        var pushRelay = new GateWayPushRelay();
        var sessionTable = new GateWaySessionTable(
            poolRegistry,
            new DefaultBackendClientFactory(),
            new TcpBackendConnector(),
            pushRelay);
        var router = new GateWayRouter(loop, config, poolRegistry, sessionTable);
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var server = new CRpcServer(loop, new CRpcServerOptions
        {
            Port = config.ListenPort,
            HandlerFactory = srv => new GateWayServerHandler(srv, sessionTable, config.FallbackServiceId),
        });

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            loop.RegisterService(new GateWayServiceImpl(router));
            await server.StartAsync(cts.Token);
        });

        Console.WriteLine($"GateWay listening on {config.ListenPort}");
        foreach (var serviceId in poolRegistry.ServiceIds)
        {
            if (poolRegistry.TryGetPool(serviceId, out var pool))
            {
                var endpoints = string.Join(", ", pool.Endpoints.Select(endpoint => endpoint.ToString()));
                Console.WriteLine($"GateWay pool serviceId={serviceId}: {endpoints}");
            }
        }

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

    private static string? ResolveConfigPath(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--config" && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        var defaultPath = Path.Combine(AppContext.BaseDirectory, "gateway.json");
        if (File.Exists(defaultPath))
        {
            return defaultPath;
        }

        var siblingPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "gateway.json"));
        return File.Exists(siblingPath) ? siblingPath : defaultPath;
    }
}
