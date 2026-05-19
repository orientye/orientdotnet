using System.Runtime.ExceptionServices;

namespace CRpc.Async;

public static class CRpcLoopRunner
{
    public static void RunUntilComplete(CRpcLoop loop, Func<CRpcTask> operation, int sleepMilliseconds = 1)
    {
        RunUntilComplete(
            loop,
            async () =>
            {
                await operation();
                return 0;
            },
            sleepMilliseconds);
    }

    public static T RunUntilComplete<T>(CRpcLoop loop, Func<CRpcTask<T>> operation, int sleepMilliseconds = 1)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(operation);
        if (sleepMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sleepMilliseconds));
        }

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

            if (!completed && sleepMilliseconds > 0)
            {
                Thread.Sleep(sleepMilliseconds);
            }
        }

        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        return result!;
    }
}
