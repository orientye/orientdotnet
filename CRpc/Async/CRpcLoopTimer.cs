namespace CRpc.Async;

internal sealed class CRpcLoopTimer
{
    private readonly Action action;

    public CRpcLoopTimer(Action action)
    {
        this.action = action;
    }

    public bool IsCanceled { get; private set; }

    public void Cancel()
    {
        IsCanceled = true;
    }

    public void Invoke()
    {
        if (!IsCanceled)
        {
            action();
        }
    }
}
