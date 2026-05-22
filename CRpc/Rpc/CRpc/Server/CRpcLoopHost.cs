using CRpc.Async;

namespace CRpc.Rpc.CRpc.Server;

public static class CRpcLoopHost
{
    public static void RunUntilCancelled(CRpcLoop loop, CancellationToken cancellationToken)
    {
        CRpcServerLoop.RunUntilCancelled(loop, cancellationToken);
    }

    [Obsolete("Use RunUntilCancelled(loop, cancellationToken). sleepMilliseconds is no longer used.")]
    public static void RunUntilCancelled(CRpcLoop loop, CancellationToken cancellationToken, int sleepMilliseconds)
    {
        RunUntilCancelled(loop, cancellationToken);
    }
}
