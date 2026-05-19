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

loop.Post(() => loop.RegisterService(new HelloworldServiceImpl()));
loop.Tick();

var crpcServer = new CRpcServer(loop, new CRpcServerOptions { Port = 7999 });
var httpServer = new HttpServer(loop, new HttpServerOptions { Port = 8080 });

await crpcServer.StartAsync(cts.Token);
await httpServer.StartAsync(cts.Token);

Console.WriteLine("CRpc listening on 7999, HTTP JSON on 8080");
Console.WriteLine("POST http://127.0.0.1:8080/1000/1 with application/json body");

try
{
    CRpcLoopHost.RunUntilCancelled(loop, cts.Token);
}
finally
{
    await httpServer.StopAsync();
    await crpcServer.StopAsync();
}
