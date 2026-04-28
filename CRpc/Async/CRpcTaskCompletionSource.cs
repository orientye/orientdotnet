namespace CRpc.Async;

public sealed class CRpcTaskCompletionSource<T>
{
    private readonly object syncRoot = new();
    private readonly CRpcLoop loop;
    private readonly List<Action> continuations = new();
    private CRpcTaskStatus status;
    private T? result;
    private Exception? exception;

    public CRpcTaskCompletionSource(CRpcLoop? loop = null)
    {
        this.loop = loop ?? CRpcLoop.Main;
        Task = new CRpcTask<T>(this);
    }

    public CRpcTask<T> Task { get; }

    internal CRpcTaskStatus Status
    {
        get
        {
            lock (syncRoot)
            {
                return status;
            }
        }
    }

    internal bool IsCompletedOnCurrentThread => Status != CRpcTaskStatus.Pending && loop.IsInLoopThread;

    public bool TrySetResult(T result)
    {
        return TryComplete(CRpcTaskStatus.Succeeded, result, null);
    }

    public bool TrySetException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return TryComplete(CRpcTaskStatus.Faulted, default, exception);
    }

    public bool TrySetCanceled()
    {
        return TryComplete(CRpcTaskStatus.Canceled, default, null);
    }

    internal T GetResult()
    {
        lock (syncRoot)
        {
            return status switch
            {
                CRpcTaskStatus.Succeeded => result!,
                CRpcTaskStatus.Faulted => throw exception!,
                CRpcTaskStatus.Canceled => throw new TaskCanceledException(),
                _ => throw new InvalidOperationException("CRpcTask has not completed.")
            };
        }
    }

    internal void OnCompleted(Action continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        var postNow = false;
        lock (syncRoot)
        {
            if (status == CRpcTaskStatus.Pending)
            {
                continuations.Add(continuation);
            }
            else
            {
                postNow = true;
            }
        }

        if (postNow)
        {
            loop.Post(continuation);
        }
    }

    private bool TryComplete(CRpcTaskStatus status, T? result, Exception? exception)
    {
        Action[] continuationsToRun;
        lock (syncRoot)
        {
            if (this.status != CRpcTaskStatus.Pending)
            {
                return false;
            }

            this.status = status;
            this.result = result;
            this.exception = exception;
            continuationsToRun = continuations.ToArray();
            continuations.Clear();
        }

        foreach (var continuation in continuationsToRun)
        {
            loop.Post(continuation);
        }

        return true;
    }
}
