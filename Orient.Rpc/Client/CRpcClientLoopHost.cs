using Orient.Runtime;

namespace Orient.Rpc.Client;

public static class CRpcClientLoopHost
{
    public static void RunUntilCancelled(OrientLoop loop, CancellationToken cancellationToken) =>
        OrientLoopHost.RunUntilCancelled(loop, cancellationToken);
}
