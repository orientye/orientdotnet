// See https://aka.ms/new-console-template for more information

using Orient.Runtime;
using Orient.Rpc.Client;
using Example;

Console.WriteLine("Hello, RPC Client!");

var executor = new OrientExecutor();

OrientExecutorRunner.RunUntilComplete(executor, async () =>
{
    var reference = await CRpcReference
        .For<GreeterClient>()
        .Url("crpc://127.0.0.1:7999")
        .ConnectAsync(executor);

    try
    {
        var client = reference.Proxy;
        for (var i = 0; i < 5; i++)
        {
            HelloRequest req = new HelloRequest();
            req.Msg = $"hi, crpc, I am from client, call={i}";
            var (result, helloReply) = await client.SayHelloAsync(req);
            Console.WriteLine($"call={i}, server return: result={result}, response: {helloReply.Msg}");
        }
    }
    finally
    {
        await reference.CloseAsync();
        await reference.ShutdownIoAsync();
    }
});

if (!Console.IsInputRedirected)
{
    Console.ReadKey();
}
