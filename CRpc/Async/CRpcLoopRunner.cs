using System.Runtime.ExceptionServices;

namespace CRpc.Async;

public static class CRpcLoopRunner
{
    public static void RunUntilComplete(CRpcLoop loop, Func<CRpcTask> operation)
    {
        RunUntilComplete(
            loop,
            async () =>
            {
                await operation();
                return 0;
            });
    }

    public static T RunUntilComplete<T>(CRpcLoop loop, Func<CRpcTask<T>> operation)
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
                Console.Error.WriteLine($"CRpcLoopRunner: unexpected exception escaped Tick: {tickException}");
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

    [Obsolete("Use RunUntilComplete(loop, operation). sleepMilliseconds is no longer used.")]
    public static void RunUntilComplete(CRpcLoop loop, Func<CRpcTask> operation, int sleepMilliseconds)
    {
        RunUntilComplete(loop, operation);
    }

    [Obsolete("Use RunUntilComplete(loop, operation). sleepMilliseconds is no longer used.")]
    public static T RunUntilComplete<T>(CRpcLoop loop, Func<CRpcTask<T>> operation, int sleepMilliseconds)
    {
        return RunUntilComplete(loop, operation);
    }
}
