using System.Runtime.ExceptionServices;

namespace Orient.Runtime;

public static class OrientExecutorRunner
{
    public static void RunUntilComplete(OrientExecutor loop, Func<OrientTask> operation)
    {
        RunUntilComplete(
            loop,
            async () =>
            {
                await operation();
                return 0;
            });
    }

    public static T RunUntilComplete<T>(OrientExecutor loop, Func<OrientTask<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(operation);

        loop.BindToCurrentThread();

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
                loop.Tick();
            }
            catch (Exception tickException)
            {
                Console.Error.WriteLine($"OrientExecutorRunner: unexpected exception escaped Tick: {tickException}");
            }

            if (!completed)
            {
                loop.WaitForWorkOrTimer(CancellationToken.None);
            }
        }

        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        return result!;
    }
}
