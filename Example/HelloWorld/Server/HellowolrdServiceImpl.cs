using CRpc.Async;
using CRpc.Rpc.CRpc.Server;

namespace Example;

public class HelloworldServiceImpl : GreeterServiceBase
{
    protected override CRpcTask<(int, HelloReply)> SayHelloAsync(
        CRpcContext context,
        HelloRequest request)
    {
        _ = PushServerPushHelloAsync(
            context.Connection,
            new ServerHelloPush { Msg = $"server saw: {request.Msg}" });

        return CRpcTask.FromResult(
            (0, new HelloReply { Msg = $"Hello {request.Msg}" }),
            CRpcLoop.Current);
    }

    public CRpcTask<(int, HelloReply)> InvokeSayHelloAsync(CRpcContext context, HelloRequest request)
    {
        return SayHelloAsync(context, request);
    }
}
