namespace Orient.Runtime;

public static class OrientExecutorHost
{
    public static void RunUntilCancelled(OrientExecutor executor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executor);
        executor.BindToCurrentThread();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                executor.Tick();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"OrientExecutorHost: unexpected exception escaped Tick: {exception}");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                executor.WaitForWorkOrTimer(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }
}
