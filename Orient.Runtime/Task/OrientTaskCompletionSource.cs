namespace Orient.Runtime;

public sealed class OrientTaskCompletionSource<T>
{
    private readonly OrientLoop loop;
    private readonly List<Action> continuations = new();
    private OrientTaskStatus status;
    private T? result;
    private Exception? exception;

    public OrientTaskCompletionSource(OrientLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        this.loop = loop;
        Task = new OrientTask<T>(this);
    }

    public OrientTask<T> Task { get; }

    internal OrientTaskStatus Status => status;

    /// <summary>
    /// True when the task has completed and the current thread is the owner <see cref="OrientLoop"/> thread.
    /// On other threads this is always false, even if the task has already completed.
    /// </summary>
    internal bool IsCompletedOnCurrentThread => Status != OrientTaskStatus.Pending && loop.IsInLoopThread;

    public bool TrySetResult(T result)
    {
        return TryComplete(OrientTaskStatus.Succeeded, result, null);
    }

    public bool TrySetException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return TryComplete(OrientTaskStatus.Faulted, default, exception);
    }

    public bool TrySetCanceled()
    {
        return TryComplete(OrientTaskStatus.Canceled, default, null);
    }

    internal T GetResult()
    {
        return status switch
        {
            OrientTaskStatus.Succeeded => result!,
            OrientTaskStatus.Faulted => throw exception!,
            OrientTaskStatus.Canceled => throw new TaskCanceledException(),
            _ => throw new InvalidOperationException("OrientTask has not completed.")
        };
    }

    internal void OnCompleted(Action continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        EnsureAwaitOnLoopThread();

        if (status == OrientTaskStatus.Pending)
        {
            continuations.Add(continuation);
            return;
        }

        loop.Post(continuation);
    }

    private bool TryComplete(OrientTaskStatus status, T? result, Exception? exception)
    {
        EnsureLoopThread();
        if (this.status != OrientTaskStatus.Pending)
        {
            return false;
        }

        this.status = status;
        this.result = result;
        this.exception = exception;
        var continuationsToRun = continuations.ToArray();
        continuations.Clear();

        foreach (var continuation in continuationsToRun)
        {
            loop.Post(continuation);
        }

        return true;
    }

    private void EnsureLoopThread()
    {
        if (!loop.IsInLoopThread)
        {
            throw new InvalidOperationException("OrientTaskCompletionSource must be used from its OrientLoop loop thread.");
        }
    }

    private void EnsureAwaitOnLoopThread()
    {
        if (!loop.IsInLoopThread)
        {
            throw new InvalidOperationException("OrientTask must be awaited on its owner OrientLoop thread.");
        }
    }
}
