// See https://aka.ms/new-console-template for more information

using Orient.Logging;
using Orient.Runtime;
using Orient.Rpc.Client;
using Orient.Rpc.Logging;
using Example;

var sink = new ConsoleOrientLogSink();
await using var logService = new OrientLogService(sink, minLevel: OrientLogLevel.Info);
logService.Start();
using var dotNettyLogging = OrientDotNettyLogging.Install(logService);
var logger = logService.CreateLogger("HelloWorld.Client");
logger.Info(0, "Hello, RPC Client!");

var executor = new OrientExecutor(new OrientExecutorOptions { LoggerFactory = logService });

OrientExecutorRunner.RunUntilComplete(executor, async () =>
{
    var reference = await CRpcReference
        .For<GreeterClient>()
        .Url("crpc://127.0.0.1:7999")
        .ClientOptions(new CRpcClientOptions { LoggerFactory = logService })
        .ConnectAsync(executor);

    try
    {
        var client = reference.Proxy;
        client.Logger = logService.CreateLogger("HelloWorld.Client.GreeterClient");
        for (var i = 0; i < 5; i++)
        {
            HelloRequest req = new HelloRequest();
            req.Msg = $"hi, crpc, I am from client, call={i}";
            var (result, helloReply) = await client.SayHelloAsync(req);
            logger.Info(0, $"call={i}, server return: result={result}, response: {helloReply.Msg}");
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
