using CRpc.Async;

namespace CRpc.Rpc.CRpc.Client;

public static class CRpcClientLoop
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
                Console.Error.WriteLine($"CRpcClientLoop: unexpected exception escaped Tick: {exception}");
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
}
