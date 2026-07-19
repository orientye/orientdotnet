using Orient.Logging;
using Orient.Runtime;
using Orient.Rpc.Client;

namespace Example;

public sealed class GreeterClient : GreeterClientBase
{
    public IOrientLogger Logger { private get; set; } = NullOrientLogger.Instance;

    protected override OrientTask OnPushServerPushHelloAsync(
        CRpcPushContext context,
        ServerHelloPush message)
    {
        Logger.Info(0, $"server push: {message.Msg}");
        return OrientTask.CompletedTask(context.Executor);
    }
}
