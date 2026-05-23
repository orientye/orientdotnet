using CRpc.Async;

namespace CRpc.Rpc.CRpc.Server;

public static class CRpcLoopHost
{
    public static void RunUntilCancelled(CRpcLoop loop, CancellationToken cancellationToken)
    {
        CRpcServerLoop.RunUntilCancelled(loop, cancellationToken);
    }
}
