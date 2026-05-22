using CRpc.Async;

namespace CRpc.Rpc.CRpc.Server;

public static class CRpcServerLoop
{
    public static void RunUntilCancelled(CRpcLoop loop, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(loop);
        loop.BindToCurrentThread();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                loop.Tick();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"CRpcServerLoop: unexpected exception escaped Tick: {exception}");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                loop.WaitForWorkOrTimer(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    [Obsolete("Use RunUntilCancelled(loop, cancellationToken). sleepMilliseconds is no longer used.")]
    public static void RunUntilCancelled(CRpcLoop loop, CancellationToken cancellationToken, int sleepMilliseconds)
    {
        RunUntilCancelled(loop, cancellationToken);
    }
}
