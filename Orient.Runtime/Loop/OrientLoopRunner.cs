using System.Runtime.ExceptionServices;

namespace Orient.Runtime;

public static class OrientLoopRunner
{
    public static void RunUntilComplete(OrientLoop loop, Func<OrientTask> operation)
    {
        RunUntilComplete(
            loop,
            async () =>
            {
                await operation();
                return 0;
            });
    }

    public static T RunUntilComplete<T>(OrientLoop loop, Func<OrientTask<T>> operation)
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
                Console.Error.WriteLine($"OrientLoopRunner: unexpected exception escaped Tick: {tickException}");
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
