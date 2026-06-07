using CRpc.Async;
using CRpc.Rpc.CRpc.Server;
using Example;

Console.WriteLine("Hello, RPC Server!");

var loop = new CRpcLoop();
using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var crpcPort = ParsePort(args, defaultPort: 7999);
var httpPort = crpcPort == 7999 ? 8080 : crpcPort + 1000;

var crpcServer = new CRpcServer(loop, new CRpcServerOptions { Port = crpcPort });
var httpServer = new HttpServer(loop, new HttpServerOptions { Port = httpPort });

CRpcLoopRunner.RunUntilComplete(loop, async () =>
{
    loop.RegisterService(new HelloworldServiceImpl());
    await crpcServer.StartAsync(cts.Token);
    await httpServer.StartAsync(cts.Token);
});

Console.WriteLine($"CRpc listening on {crpcPort}, HTTP JSON on {httpPort}");
Console.WriteLine($"POST http://127.0.0.1:{httpPort}/1000/1 with application/json body");

try
{
    CRpcLoopHost.RunUntilCancelled(loop, cts.Token);
}
finally
{
    CRpcLoopRunner.RunUntilComplete(loop, async () =>
    {
        await httpServer.StopAsync();
        await crpcServer.StopAsync();
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
