using System.Runtime.CompilerServices;

namespace CRpc.Async;

[AsyncMethodBuilder(typeof(CRpcAsyncMethodBuilder))]
public readonly struct CRpcTask
{
    private readonly CRpcTask<CRpcUnit> task;

    internal CRpcTask(CRpcTask<CRpcUnit> task)
    {
        this.task = task;
    }

    public Awaiter GetAwaiter()
    {
        return new Awaiter(task.GetAwaiter());
    }

    public static CRpcTask<T> FromTask<T>(Task<T> task, CRpcLoop? loop = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        loop = CRpcLoop.RequireCurrentOr(loop);
        var source = new CRpcTaskCompletionSource<T>(loop);
        CompleteFromTask(task, source, loop);
        return source.Task;
    }

    public static CRpcTask<T> FromResult<T>(T result, CRpcLoop? loop = null)
    {
        loop = CRpcLoop.RequireCurrentOr(loop);
        var source = new CRpcTaskCompletionSource<T>(loop);
        source.TrySetResult(result);
        return source.Task;
    }

    public static CRpcTask CompletedTask(CRpcLoop? loop = null)
    {
        loop = CRpcLoop.RequireCurrentOr(loop);
        var source = new CRpcTaskCompletionSource<CRpcUnit>(loop);
        source.TrySetResult(CRpcUnit.Value);
        return new CRpcTask(source.Task);
    }

    public static CRpcTask FromTask(Task task, CRpcLoop? loop = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        loop = CRpcLoop.RequireCurrentOr(loop);
        var source = new CRpcTaskCompletionSource<CRpcUnit>(loop);
        CompleteFromTask(task, source, loop);
        return new CRpcTask(source.Task);
    }

    /// <summary>
    /// Returns a task that completes after the delay on the target loop's timer scheduler.
    /// Must be called on the bound <see cref="CRpcLoop"/> thread while the loop is driven.
    /// To schedule a delay from another thread, use <see cref="CRpcLoop.Post"/> first.
    /// </summary>
    public static CRpcTask Delay(int millisecondsDelay, CRpcLoop? loop = null)
    {
        if (millisecondsDelay < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(millisecondsDelay));
        }

        loop = CRpcLoop.RequireCurrentOr(loop);
        loop.EnsureInLoopThread();
        var source = new CRpcTaskCompletionSource<CRpcUnit>(loop);
        if (millisecondsDelay == 0)
        {
            source.TrySetResult(CRpcUnit.Value);
            return new CRpcTask(source.Task);
        }

        if (millisecondsDelay > 0)
        {
            loop.ScheduleDelay(
                millisecondsDelay,
                () => source.TrySetResult(CRpcUnit.Value));
        }

        return new CRpcTask(source.Task);
    }

    private static void CompleteFromTask<T>(Task<T> task, CRpcTaskCompletionSource<T> source, CRpcLoop loop)
    {
        if (task.IsCompleted)
        {
            CompleteOnLoop(loop, () => SetSourceResult(task, source));
            return;
        }

        task.ContinueWith(
            completedTask => loop.Post(() => SetSourceResult(completedTask, source)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void SetSourceResult<T>(Task<T> task, CRpcTaskCompletionSource<T> source)
    {
        if (task.IsCanceled)
        {
            source.TrySetCanceled();
            return;
        }

        if (task.IsFaulted)
        {
            Exception exception;
            if (task.Exception?.InnerException is not null)
            {
                exception = task.Exception.InnerException;
            }
            else if (task.Exception is not null)
            {
                exception = task.Exception;
            }
            else
            {
                exception = new InvalidOperationException("Task failed.");
            }

            source.TrySetException(exception);
            return;
        }

        source.TrySetResult(task.Result);
    }

    private static void CompleteFromTask(Task task, CRpcTaskCompletionSource<CRpcUnit> source, CRpcLoop loop)
    {
        if (task.IsCompleted)
        {
            CompleteOnLoop(loop, () => SetSourceResult(task, source));
            return;
        }

        task.ContinueWith(
            completedTask => loop.Post(() => SetSourceResult(completedTask, source)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void CompleteOnLoop(CRpcLoop loop, Action complete)
    {
        if (loop.IsInLoopThread)
        {
            complete();
            return;
        }

        loop.Post(complete);
    }

    private static void SetSourceResult(Task task, CRpcTaskCompletionSource<CRpcUnit> source)
    {
        if (task.IsCanceled)
        {
            source.TrySetCanceled();
            return;
        }

        if (task.IsFaulted)
        {
            Exception exception;
            if (task.Exception?.InnerException is not null)
            {
                exception = task.Exception.InnerException;
            }
            else if (task.Exception is not null)
            {
                exception = task.Exception;
            }
            else
            {
                exception = new InvalidOperationException("Task failed.");
            }

            source.TrySetException(exception);
            return;
        }

        source.TrySetResult(CRpcUnit.Value);
    }

    public readonly struct Awaiter : INotifyCompletion
    {
        private readonly CRpcTask<CRpcUnit>.Awaiter awaiter;

        internal Awaiter(CRpcTask<CRpcUnit>.Awaiter awaiter)
        {
            this.awaiter = awaiter;
        }

        public bool IsCompleted => awaiter.IsCompleted;

        public void OnCompleted(Action continuation)
        {
            awaiter.OnCompleted(continuation);
        }

        public void GetResult()
        {
            awaiter.GetResult();
        }
    }
}

internal readonly struct CRpcUnit
{
    public static CRpcUnit Value { get; } = new();
}
