using System.Collections.Concurrent;
using Orient.Runtime;

namespace Orient.TestHelper;

/// <summary>
/// Runs a single <see cref="OrientExecutor"/> on a dedicated background thread (for handler / Post + Tick tests).
/// </summary>
public sealed class ExecutorTestDriver : IDisposable
{
    private readonly OrientExecutor executor;
    private readonly BlockingCollection<Action> queue = new();
    private readonly Thread thread;
    private volatile bool disposed;

    public ExecutorTestDriver(OrientExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        this.executor = executor;
        thread = new Thread(DriverMain)
        {
            IsBackground = true,
            Name = "ExecutorTestDriver",
        };
        thread.Start();
    }

    public void Run(Action action)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(action);

        using var done = new ManualResetEventSlim(false);
        Exception? captured = null;
        queue.Add(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
            finally
            {
                done.Set();
            }
        });

        if (!done.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("ExecutorTestDriver.Run timed out.");
        }

        if (captured is not null)
        {
            throw captured;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        queue.CompleteAdding();
        if (!thread.Join(TimeSpan.FromSeconds(2)))
        {
            throw new InvalidOperationException("ExecutorTestDriver thread did not exit.");
        }
    }

    private void DriverMain()
    {
        executor.BindToCurrentThread();
        try
        {
            foreach (var action in queue.GetConsumingEnumerable())
            {
                action();
            }
        }
        finally
        {
#if DEBUG
            OrientExecutor.ResetDebugThreadBindingForTests();
#endif
        }
    }
}
