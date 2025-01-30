using CoreRPC.Rpc.CRpc;
using CoreRPC.Rpc.CRpc.Server;

namespace Example;

public class HelloworldServiceImpl : GreeterBase
{
    protected override Task<(int, Example.HelloReply)> SayHelloAsync(CRpcContext context, Example.HelloRequest request)
    {
        var resp = new HelloReply();
        var tm = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        resp.Msg = $"echo from server, tm={tm}";
        Console.WriteLine($"request form client={request.Msg}");
        Console.WriteLine($"************ {tm} **********RPC result={resp.Msg}***************");
        return Task.FromResult((0, resp));
    }
}