using Orient.Logging;
using Orient.Runtime;
using Orient.Rpc.Logging;
using Orient.Rpc.Server;

namespace GateWay;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var sink = new ConsoleOrientLogSink();
        await using var logService = new OrientLogService(sink, minLevel: OrientLogLevel.Info);
        logService.Start();
        using var dotNettyLogging = OrientDotNettyLogging.Install(logService);
        var logger = logService.CreateLogger("GateWay.Server");
        logger.Info(0, "GateWay starting...");

        var configPath = ResolveConfigPath(args);
        var config = GateWayConfigLoader.LoadOrDefault(
            configPath,
            logService.CreateLogger("GateWay.Config"));
        var poolRegistry = config.BuildRegistry();

        var executor = new OrientExecutor(new OrientExecutorOptions { LoggerFactory = logService });
        var pushRelay = new GateWayPushRelay();
        var sessionTable = new GateWaySessionTable(
            poolRegistry,
            new DefaultBackendClientFactory(logService),
            new TcpBackendConnector(),
            pushRelay);
        var router = new GateWayRouter(executor, config, poolRegistry, sessionTable);
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var server = new CRpcServer(executor, new CRpcServerOptions
        {
            Port = config.ListenPort,
            LoggerFactory = logService,
            HandlerFactory = srv => new GateWayServerHandler(
                srv,
                sessionTable,
                config.FallbackServiceId,
                logService.CreateLogger("GateWay.ServerHandler")),
        });

        OrientExecutorRunner.RunUntilComplete(executor, async () =>
        {
            server.Services.Register(new GateWayServiceImpl(router));
            await server.StartAsync(cts.Token);
        });

        logger.Info(0, $"GateWay listening on {config.ListenPort}");
        foreach (var serviceId in poolRegistry.ServiceIds)
        {
            if (poolRegistry.TryGetPool(serviceId, out var pool))
            {
                var endpoints = string.Join(", ", pool.Endpoints.Select(endpoint => endpoint.ToString()));
                logger.Info(0, $"GateWay pool serviceId={serviceId}: {endpoints}");
            }
        }

        try
        {
            OrientExecutorHost.RunUntilCancelled(executor, cts.Token);
        }
        finally
        {
            OrientExecutorRunner.RunUntilComplete(executor, async () =>
            {
                await sessionTable.DisposeAllAsync();
                await server.StopAsync();
            });
        }

        logger.Info(0, "GateWay stopped.");
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
