// See https://aka.ms/new-console-template for more information
using CRpc.Async;
using CRpc.Rpc.CRpc.Server;
using Example;

Console.WriteLine("Hello, RPC Server!");

var loop = new CRpcLoop();
var server = new CRpcServer(loop);
loop.Post(() => server.RegisterService(new HelloworldServiceImpl()));
await server.RunAsync();

Console.ReadKey();