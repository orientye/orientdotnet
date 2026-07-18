using System.Runtime.CompilerServices;

namespace Orient.Runtime;

[AsyncMethodBuilder(typeof(OrientAsyncMethodBuilder))]
public readonly struct OrientTask
{
    private readonly OrientTask<OrientUnit> task;

    internal OrientTask(OrientTask<OrientUnit> task)
    {
        this.task = task;
    }

    public Awaiter GetAwaiter()
    {
        return new Awaiter(task.GetAwaiter());
    }

    public static OrientTask<T> FromTask<T>(Task<T> task, OrientExecutor? loop = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        loop = OrientExecutor.RequireCurrentOr(loop);
        var source = new OrientTaskCompletionSource<T>(loop);
        CompleteFromTask(task, source, loop);
        return source.Task;
    }

    public static OrientTask<T> FromResult<T>(T result, OrientExecutor? loop = null)
    {
        loop = OrientExecutor.RequireCurrentOr(loop);
        var source = new OrientTaskCompletionSource<T>(loop);
        source.TrySetResult(result);
        return source.Task;
    }

    public static OrientTask CompletedTask(OrientExecutor? loop = null)
    {
        loop = OrientExecutor.RequireCurrentOr(loop);
        var source = new OrientTaskCompletionSource<OrientUnit>(loop);
        source.TrySetResult(OrientUnit.Value);
        return new OrientTask(source.Task);
    }

    public static OrientTask FromTask(Task task, OrientExecutor? loop = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        loop = OrientExecutor.RequireCurrentOr(loop);
        var source = new OrientTaskCompletionSource<OrientUnit>(loop);
        CompleteFromTask(task, source, loop);
        return new OrientTask(source.Task);
    }

    /// <summary>
    /// Returns a task that completes after the delay on the target executor's timer scheduler.
    /// Must be called on the bound <see cref="OrientExecutor"/> thread while the executor is driven.
    /// To schedule a delay from another thread, use <see cref="OrientExecutor.Post"/> first.
    /// </summary>
    public static OrientTask Delay(int millisecondsDelay, OrientExecutor? loop = null)
    {
        if (millisecondsDelay < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(millisecondsDelay));
        }

        loop = OrientExecutor.RequireCurrentOr(loop);
        loop.EnsureInExecutorThread();
        var source = new OrientTaskCompletionSource<OrientUnit>(loop);
        if (millisecondsDelay == 0)
        {
            source.TrySetResult(OrientUnit.Value);
            return new OrientTask(source.Task);
        }

        if (millisecondsDelay > 0)
        {
            loop.ScheduleDelay(
                millisecondsDelay,
                () => source.TrySetResult(OrientUnit.Value));
        }

        return new OrientTask(source.Task);
    }

    private static void CompleteFromTask<T>(Task<T> task, OrientTaskCompletionSource<T> source, OrientExecutor loop)
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

    private static void SetSourceResult<T>(Task<T> task, OrientTaskCompletionSource<T> source)
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

    private static void CompleteFromTask(Task task, OrientTaskCompletionSource<OrientUnit> source, OrientExecutor loop)
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

    private static void CompleteOnLoop(OrientExecutor loop, Action complete)
    {
        if (loop.IsInExecutorThread)
        {
            complete();
            return;
        }

        loop.Post(complete);
    }

    private static void SetSourceResult(Task task, OrientTaskCompletionSource<OrientUnit> source)
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

        source.TrySetResult(OrientUnit.Value);
    }

    public readonly struct Awaiter : INotifyCompletion
    {
        private readonly OrientTask<OrientUnit>.Awaiter awaiter;

        internal Awaiter(OrientTask<OrientUnit>.Awaiter awaiter)
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

internal readonly struct OrientUnit
{
    public static OrientUnit Value { get; } = new();
}
