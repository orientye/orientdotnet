// See https://aka.ms/new-console-template for more information
using CoreRPC.Rpc.CRpc.Server;
using Example;

Console.WriteLine("Hello, RPC Server!");

CRpcServer server = new CRpcServer();
server.RegisterService(new HelloworldServiceImpl());
await server.RunAsync();

Console.ReadKey();