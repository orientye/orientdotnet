using CRpc.Async;

namespace CRPC.Tests;

internal static class DedicatedLoopThread
{
    public static void Run(CRpcLoop loop, Action<CRpcLoop> action)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(action);

        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                loop.BindToCurrentThread();
                action(loop);
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        })
        {
            IsBackground = true,
            Name = "CRpcLoop dedicated test thread",
        };

        thread.Start();
        thread.Join();

        if (captured is not null)
        {
            throw captured;
        }
    }
}
