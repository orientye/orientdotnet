using CRpc.Async;
using CRpc.Rpc.CRpc.Client;

namespace Example;

public sealed class GreeterClient : GreeterClientBase
{
    protected override CRpcTask OnPushServerPushHelloAsync(
        CRpcPushContext context,
        ServerHelloPush message)
    {
        Console.WriteLine($"server push: {message.Msg}");
        return CRpcTask.CompletedTask(context.Loop);
    }
}
