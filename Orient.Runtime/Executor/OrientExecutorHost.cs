using Orient.Logging;

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
                const string message = "OrientExecutorHost: unexpected exception escaped Tick";
                if (executor.Logger.IsEnabled(OrientLogLevel.Error))
                {
                    executor.Logger.Log(OrientLogLevel.Error, 1002, message, exception);
                }
                else
                {
                    Console.Error.WriteLine($"{message}: {exception}");
                }
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
