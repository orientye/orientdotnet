// See https://aka.ms/new-console-template for more information

using System.Net;
using CRpc.Rpc.CRpc.Client;
using Example;

Console.WriteLine("Hello, RPC Client!");

// ReferenceConfig<GreetingsService> reference =
//     ReferenceBuilder.<GreetingsService>newBuilder()
//     .interfaceClass(GreetingsService.class)
//     .url("tri://localhost:50052")
//     .build();
// DubboBootstrap.getInstance().reference(reference).start();
// GreetingsService service = reference.get();
//
// String message = service.sayHi("dubbo");


//auto proxy = ::trpc::GetTrpcClient()->GetProxy<::trpc::test::helloworld::GreeterServiceProxy>(FLAGS_service_name);
//::trpc::Status status = proxy->SayHello(client_ctx, req, &rsp);

CRpcClient rpcClient = new CRpcClient();
await rpcClient.ConnectAsync(IPAddress.Loopback, 7999);
GreeterClient client = new GreeterClient();
client.__client = rpcClient;
HelloRequest req = new HelloRequest();
req.Msg = "hi, crpc, I am from client";
var (result, helloReply) = await client.SayHelloAsync(req);
Console.WriteLine($"server return: result={result}, response: {helloReply.Msg}");
Console.ReadKey();