using Orient.Runtime;
using Orient.Rpc.Server;
using Example;
using Example.Http;

Console.WriteLine("Hello, RPC Server!");

var executor = new OrientExecutor();
using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var unified = args.Contains("--unified");
var withHttp = args.Contains("--http");
var crpcPort = ParsePort(args, defaultPort: 7999);
var httpPort = crpcPort == 7999 ? 8080 : crpcPort + 1000;

var impl = new HelloworldServiceImpl();
var crpcServer = new CRpcServer(executor, new CRpcServerOptions { Port = crpcPort });
HttpListenServer? httpListen = null;
UnifiedServer? unifiedServer = null;

if (unified)
{
    unifiedServer = new UnifiedServer(executor, crpcServer, impl, crpcPort);
}
else if (withHttp)
{
    httpListen = new HttpListenServer(executor, crpcServer, impl, httpPort);
}

OrientExecutorRunner.RunUntilComplete(executor, async () =>
{
    crpcServer.Services.Register(impl);

    if (unifiedServer is not null)
    {
        await unifiedServer.StartAsync(cts.Token);
        Console.WriteLine($"Unified CRpc+HTTP listening on {crpcPort}");
        Console.WriteLine($"POST http://127.0.0.1:{crpcPort}/api/greeter/say-hello");
    }
    else if (httpListen is not null)
    {
        await crpcServer.StartAsync(cts.Token);
        await httpListen.StartAsync(cts.Token);
        Console.WriteLine($"CRpc listening on {crpcPort}, HTTP demo on {httpPort}");
        Console.WriteLine($"POST http://127.0.0.1:{httpPort}/api/greeter/say-hello");
    }
    else
    {
        await crpcServer.StartAsync(cts.Token);
        Console.WriteLine($"CRpc listening on {crpcPort}");
    }
});

try
{
    OrientExecutorHost.RunUntilCancelled(executor, cts.Token);
}
finally
{
    OrientExecutorRunner.RunUntilComplete(executor, async () =>
    {
        if (unifiedServer is not null)
        {
            await unifiedServer.StopAsync();
        }
        else
        {
            if (httpListen is not null)
            {
                await httpListen.StopAsync();
            }

            await crpcServer.StopAsync();
        }
    });
}

static int ParsePort(string[] args, int defaultPort)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var port) && port > 0)
        {
            return port;
        }
    }

    return defaultPort;
}
