using CRpc.Async;

namespace CRpc.Rpc.CRpc.Client;

public static class CRpcClientLoopHost
{
    public static void RunUntilCancelled(CRpcLoop loop, CancellationToken cancellationToken)
    {
        CRpcClientLoop.RunUntilCancelled(loop, cancellationToken);
    }
}
