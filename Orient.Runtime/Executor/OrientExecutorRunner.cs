using System.Runtime.ExceptionServices;
using Orient.Logging;

namespace Orient.Runtime;

public static class OrientExecutorRunner
{
    public static void RunUntilComplete(OrientExecutor executor, Func<OrientTask> operation)
    {
        RunUntilComplete(
            executor,
            async () =>
            {
                await operation();
                return 0;
            });
    }

    public static T RunUntilComplete<T>(OrientExecutor executor, Func<OrientTask<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(operation);

        executor.BindToCurrentThread();

        var task = operation();
        var awaiter = task.GetAwaiter();
        if (awaiter.IsCompleted)
        {
            return awaiter.GetResult();
        }

        var completed = false;
        T? result = default;
        Exception? exception = null;

        awaiter.OnCompleted(() =>
        {
            try
            {
                result = awaiter.GetResult();
            }
            catch (Exception caughtException)
            {
                exception = caughtException;
            }
            finally
            {
                completed = true;
            }
        });

        while (!completed)
        {
            try
            {
                executor.Tick();
            }
            catch (Exception tickException)
            {
                const string message = "OrientExecutorRunner: unexpected exception escaped Tick";
                if (executor.Logger.IsEnabled(OrientLogLevel.Error))
                {
                    executor.Logger.Log(OrientLogLevel.Error, 1003, message, tickException);
                }
                else
                {
                    Console.Error.WriteLine($"{message}: {tickException}");
                }
            }

            if (!completed)
            {
                executor.WaitForWorkOrTimer(CancellationToken.None);
            }
        }

        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        return result!;
    }
}
