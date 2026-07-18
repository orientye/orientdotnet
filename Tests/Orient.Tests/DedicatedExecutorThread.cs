using Orient.Runtime;

namespace Orient.Tests;

internal static class DedicatedExecutorThread
{
    public static void Run(OrientExecutor executor, Action<OrientExecutor> action)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(action);

        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                executor.BindToCurrentThread();
                action(executor);
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        })
        {
            IsBackground = true,
            Name = "OrientExecutor dedicated test thread",
        };

        thread.Start();
        thread.Join();

        if (captured is not null)
        {
            throw captured;
        }
    }
}
