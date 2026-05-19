using CRpc.Async;

namespace CRpc.Rpc.CRpc.Server;

public static class CRpcLoopHost
{
    public static void RunUntilCancelled(CRpcLoop loop, CancellationToken cancellationToken, int sleepMilliseconds = 1)
    {
        CRpcServerLoop.RunUntilCancelled(loop, cancellationToken, sleepMilliseconds);
    }
}
