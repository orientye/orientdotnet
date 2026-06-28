using Orient.Runtime;

namespace Orient.Rpc.Server;

public static class CRpcLoopHost
{
    public static void RunUntilCancelled(OrientLoop loop, CancellationToken cancellationToken) =>
        OrientLoopHost.RunUntilCancelled(loop, cancellationToken);
}
