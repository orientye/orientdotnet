namespace Orient.Runtime;

public static class OrientLoopHost
{
    public static void RunUntilCancelled(OrientLoop loop, CancellationToken cancellationToken)
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
                Console.Error.WriteLine($"OrientLoopHost: unexpected exception escaped Tick: {exception}");
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
