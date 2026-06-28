using Orient.Runtime;
using Orient.Rpc.Client;

namespace Example;

public sealed class GreeterClient : GreeterClientBase
{
    protected override OrientTask OnPushServerPushHelloAsync(
        CRpcPushContext context,
        ServerHelloPush message)
    {
        Console.WriteLine($"server push: {message.Msg}");
        return OrientTask.CompletedTask(context.Loop);
    }
}
