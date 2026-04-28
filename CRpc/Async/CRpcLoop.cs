using System.Collections.Concurrent;

namespace CRpc.Async;

public sealed class CRpcLoop
{
    [ThreadStatic]
    private static CRpcLoop? current;

    public static CRpcLoop Main { get; } = new();

    public static CRpcLoop? Current => current;

    private readonly ConcurrentQueue<Action> actions = new();
    private int threadId;

    public bool IsInLoopThread => threadId != 0 && Environment.CurrentManagedThreadId == threadId;

    public void BindToCurrentThread()
    {
        threadId = Environment.CurrentManagedThreadId;
        current = this;
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        actions.Enqueue(action);
    }

    public void Tick(int maxActions = 1024)
    {
        if (threadId == 0)
        {
            BindToCurrentThread();
        }
        else
        {
            current = this;
        }

        for (var i = 0; i < maxActions && actions.TryDequeue(out var action); i++)
        {
            action();
        }
    }
}
