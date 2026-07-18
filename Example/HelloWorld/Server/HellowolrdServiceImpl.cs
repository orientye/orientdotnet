using Orient.Runtime;
using Orient.Rpc.Server;

namespace Example;

public class HelloworldServiceImpl : GreeterServiceBase
{
    protected override OrientTask<(int, HelloReply)> SayHelloAsync(
        CRpcContext context,
        HelloRequest request)
    {
        _ = PushServerPushHelloAsync(
            context.Connection,
            new ServerHelloPush { Msg = $"server saw: {request.Msg}" });

        return OrientTask.FromResult(
            (0, new HelloReply { Msg = $"Hello {request.Msg}" }),
            OrientExecutor.Current);
    }

    public OrientTask<(int, HelloReply)> InvokeSayHelloAsync(CRpcContext context, HelloRequest request)
    {
        return SayHelloAsync(context, request);
    }
}
