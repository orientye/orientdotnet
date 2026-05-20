using System.Collections.Concurrent;
using CRpc.Async;

namespace CRPC.Tests;

/// <summary>
/// Runs a single <see cref="CRpcLoop"/> on a dedicated background thread (for handler / Post + Tick tests).
/// </summary>
internal sealed class LoopTestDriver : IDisposable
{
    private readonly CRpcLoop loop;
    private readonly BlockingCollection<Action> queue = new();
    private readonly Thread thread;
    private volatile bool disposed;

    public LoopTestDriver(CRpcLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        this.loop = loop;
        thread = new Thread(DriverMain)
        {
            IsBackground = true,
            Name = "LoopTestDriver",
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
            throw new TimeoutException("LoopTestDriver.Run timed out.");
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
            throw new InvalidOperationException("LoopTestDriver thread did not exit.");
        }
    }

    private void DriverMain()
    {
        loop.BindToCurrentThread();
        try
        {
            foreach (var action in queue.GetConsumingEnumerable())
            {
                action();
            }
        }
        finally
        {
            CRpcLoop.ResetDebugThreadBindingForTests();
        }
    }
}
