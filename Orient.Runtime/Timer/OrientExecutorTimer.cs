namespace Orient.Runtime;

internal sealed class OrientExecutorTimer
{
    private readonly Action action;
    private Action? removeFromScheduler;

    public OrientExecutorTimer(Action action)
    {
        this.action = action;
    }

    public bool IsCanceled { get; private set; }

    internal int HeapIndex { get; set; } = -1;

    internal void BindToScheduler(Action removeFromScheduler)
    {
        this.removeFromScheduler = removeFromScheduler;
    }

    internal void UnbindFromScheduler()
    {
        removeFromScheduler = null;
    }

    public void Cancel()
    {
        if (IsCanceled)
        {
            return;
        }

        IsCanceled = true;
        removeFromScheduler?.Invoke();
        removeFromScheduler = null;
    }

    public void Invoke()
    {
        if (!IsCanceled)
        {
            action();
        }
    }
}
