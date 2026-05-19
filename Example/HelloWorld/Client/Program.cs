// See https://aka.ms/new-console-template for more information

using System.Net;
using CRpc.Async;
using CRpc.Rpc.CRpc.Client;
using Example;

Console.WriteLine("Hello, RPC Client!");

await using CRpcClient rpcClient = new CRpcClient();
await rpcClient.ConnectAsync(IPAddress.Loopback, 7999);
GreeterClient client = new GreeterClient();
client.__client = rpcClient;

var loop = new CRpcLoop();
CRpcLoopRunner.RunUntilComplete(loop, RunClientAsync);

if (!Console.IsInputRedirected)
{
    Console.ReadKey();
}

async CRpcTask<int> RunClientAsync()
{
    for (var i = 0; i < 5; i++)
    {
        HelloRequest req = new HelloRequest();
        req.Msg = $"hi, crpc, I am from client, call={i}";
        var (result, helloReply) = await client.SayHelloAsync(req);
        Console.WriteLine($"call={i}, server return: result={result}, response: {helloReply.Msg}");
    }

    return 0;
}
