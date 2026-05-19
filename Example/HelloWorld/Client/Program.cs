// See https://aka.ms/new-console-template for more information

using CRpc.Async;
using CRpc.Rpc.CRpc.Client;
using Example;

Console.WriteLine("Hello, RPC Client!");

var loop = new CRpcLoop();
await using var reference = await CRpcReference
    .For<GreeterClient>()
    .Url("crpc://127.0.0.1:7999")
    .ConnectAsync(loop);

var client = reference.Proxy;

CRpcLoopRunner.RunUntilComplete(loop, async () =>
{
    for (var i = 0; i < 5; i++)
    {
        HelloRequest req = new HelloRequest();
        req.Msg = $"hi, crpc, I am from client, call={i}";
        var (result, helloReply) = await client.SayHelloAsync(req);
        Console.WriteLine($"call={i}, server return: result={result}, response: {helloReply.Msg}");
    }
});

if (!Console.IsInputRedirected)
{
    Console.ReadKey();
}
