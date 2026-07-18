using Orient.Runtime;

namespace Orient.Tests;

public class OrientExecutorInvokeAsyncTests : OrientTestBase
{
    [Fact]
    public void InvokeAsyncWithoutCurrentLoopThrowsRequireCurrentOrMessage()
    {
        var targetLoop = new OrientExecutor();
        var exception = RunOnFreshThread(() =>
            OrientExecutor.InvokeAsync(targetLoop, () => 1));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("OrientExecutor must be provided", exception!.Message);
    }

    [Fact]
    public void InvokeAsyncNullTargetLoopThrows()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        Assert.Throws<ArgumentNullException>(() =>
            OrientExecutor.InvokeAsync(null!, () => 1));
    }

    [Fact]
    public void InvokeAsyncNullActionThrows()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        Assert.Throws<ArgumentNullException>(() =>
            OrientExecutor.InvokeAsync(executor, (Func<int>)null!));
    }

    [Fact]
    public void InvokeAsyncSyncValueRunsOnTargetLoopAndCompletesOnCallerLoop()
    {
        var callerLoop = new OrientExecutor();
        var targetLoop = new OrientExecutor();
        using var targetPump = new TargetLoopPump(targetLoop);
        using var callerDriver = new ExecutorTestDriver(callerLoop);

        int? actionThreadId = null;
        int? continuationThreadId = null;
        int? result = null;

        callerDriver.Run(() =>
        {
            var callerThreadId = Environment.CurrentManagedThreadId;
            var task = OrientExecutor.InvokeAsync(
                targetLoop,
                () =>
                {
                    actionThreadId = Environment.CurrentManagedThreadId;
                    return 42;
                });

            var awaiter = task.GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                continuationThreadId = Environment.CurrentManagedThreadId;
                result = awaiter.GetResult();
            });

            PumpCallerUntil(callerLoop, () => result is not null, TimeSpan.FromSeconds(2));

            Assert.Equal(callerThreadId, continuationThreadId);
            Assert.NotEqual(callerThreadId, actionThreadId);
        });

        targetPump.EnsureNoFailure();
        Assert.Equal(42, result);
    }

    [Fact]
    public void InvokeAsyncSyncVoidRunsOnTargetLoopAndCompletesOnCallerLoop()
    {
        var callerLoop = new OrientExecutor();
        var targetLoop = new OrientExecutor();
        using var targetPump = new TargetLoopPump(targetLoop);
        using var callerDriver = new ExecutorTestDriver(callerLoop);

        int? actionThreadId = null;
        var completed = false;

        callerDriver.Run(() =>
        {
            var callerThreadId = Environment.CurrentManagedThreadId;
            var task = OrientExecutor.InvokeAsync(
                targetLoop,
                () => actionThreadId = Environment.CurrentManagedThreadId);

            var awaiter = task.GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                awaiter.GetResult();
                completed = true;
            });

            PumpCallerUntil(callerLoop, () => completed, TimeSpan.FromSeconds(2));
            Assert.NotEqual(callerThreadId, actionThreadId);
        });

        Assert.True(completed);
    }

    [Fact]
    public void InvokeAsyncAsyncValueRunsOnTargetLoopAndResumesOnCallerLoop()
    {
        var callerLoop = new OrientExecutor();
        var targetLoop = new OrientExecutor();
        using var targetPump = new TargetLoopPump(targetLoop);
        using var callerDriver = new ExecutorTestDriver(callerLoop);

        int? actionThreadId = null;
        int? continuationThreadId = null;
        int? result = null;

        callerDriver.Run(() =>
        {
            var callerThreadId = Environment.CurrentManagedThreadId;
            var task = OrientExecutor.InvokeAsync(
                targetLoop,
                async () =>
                {
                    actionThreadId = Environment.CurrentManagedThreadId;
                    await OrientTask.Delay(1, targetLoop);
                    return 7;
                });

            var awaiter = task.GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                continuationThreadId = Environment.CurrentManagedThreadId;
                result = awaiter.GetResult();
            });

            PumpCallerUntil(callerLoop, () => result is not null, TimeSpan.FromSeconds(2));
            Assert.Equal(callerThreadId, continuationThreadId);
            Assert.NotEqual(callerThreadId, actionThreadId);
        });

        Assert.Equal(7, result);
    }

    [Fact]
    public void InvokeAsyncAsyncVoidRunsOnTargetLoopAndCompletesOnCallerLoop()
    {
        var callerLoop = new OrientExecutor();
        var targetLoop = new OrientExecutor();
        using var targetPump = new TargetLoopPump(targetLoop);
        using var callerDriver = new ExecutorTestDriver(callerLoop);

        int? actionThreadId = null;
        var completed = false;

        callerDriver.Run(() =>
        {
            var callerThreadId = Environment.CurrentManagedThreadId;
            var task = OrientExecutor.InvokeAsync(
                targetLoop,
                async () =>
                {
                    actionThreadId = Environment.CurrentManagedThreadId;
                    await OrientTask.Delay(1, targetLoop);
                });

            var awaiter = task.GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                awaiter.GetResult();
                completed = true;
            });

            PumpCallerUntil(callerLoop, () => completed, TimeSpan.FromSeconds(2));
            Assert.NotEqual(callerThreadId, actionThreadId);
        });

        Assert.True(completed);
    }

    [Fact]
    public void InvokeAsyncSameLoopSyncCompletesBeforeReturn()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        var task = OrientExecutor.InvokeAsync(executor, () => 99);
        var awaiter = task.GetAwaiter();

        Assert.True(awaiter.IsCompleted);
        Assert.Equal(99, awaiter.GetResult());
    }

    [Fact]
    public void InvokeAsyncSyncExceptionFaultsCallerTask()
    {
        var callerLoop = new OrientExecutor();
        var targetLoop = new OrientExecutor();
        using var targetPump = new TargetLoopPump(targetLoop);
        using var callerDriver = new ExecutorTestDriver(callerLoop);

        Exception? captured = null;

        callerDriver.Run(() =>
        {
            var failure = new InvalidOperationException("sync boom");
            var task = OrientExecutor.InvokeAsync<int>(
                targetLoop,
                (Func<int>)(() => throw failure));

            var awaiter = task.GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                try
                {
                    awaiter.GetResult();
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            });

            PumpCallerUntil(callerLoop, () => captured is not null, TimeSpan.FromSeconds(2));
        });

        Assert.IsType<InvalidOperationException>(captured);
        Assert.Equal("sync boom", captured!.Message);
    }

    [Fact]
    public void InvokeAsyncAsyncTargetTaskExceptionFaultsCallerTask()
    {
        var callerLoop = new OrientExecutor();
        var targetLoop = new OrientExecutor();
        using var targetPump = new TargetLoopPump(targetLoop);
        using var callerDriver = new ExecutorTestDriver(callerLoop);

        Exception? captured = null;

        callerDriver.Run(() =>
        {
            var failure = new InvalidOperationException("async boom");
            var task = OrientExecutor.InvokeAsync(
                targetLoop,
                () => FaultingTargetAsync(failure, targetLoop));

            var awaiter = task.GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                try
                {
                    awaiter.GetResult();
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            });

            PumpCallerUntil(callerLoop, () => captured is not null, TimeSpan.FromSeconds(2));
        });

        Assert.IsType<InvalidOperationException>(captured);
        Assert.Equal("async boom", captured!.Message);
    }

    [Fact]
    public void InvokeAsyncTargetRunnerExceptionFaultsCallerTaskInsteadOfUnhandledException()
    {
        var callerLoop = new OrientExecutor();
        var targetLoop = new OrientExecutor();
        using var targetPump = new TargetLoopPump(targetLoop);
        using var callerDriver = new ExecutorTestDriver(callerLoop);

        Exception? unhandled = null;
        Exception? captured = null;

        targetLoop.UnhandledException += ex => unhandled = ex;

        callerDriver.Run(() =>
        {
            var failure = new InvalidOperationException("runner boom");
            var task = OrientExecutor.InvokeAsync(
                targetLoop,
                () => throw failure);

            var awaiter = task.GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                try
                {
                    awaiter.GetResult();
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            });

            PumpCallerUntil(callerLoop, () => captured is not null, TimeSpan.FromSeconds(2));
        });

        Assert.Null(unhandled);
        Assert.Equal("runner boom", captured!.Message);
    }

    [Fact]
    public void InvokeAsyncCanceledTargetTaskCancelsCallerTask()
    {
        var callerLoop = new OrientExecutor();
        var targetLoop = new OrientExecutor();
        using var targetPump = new TargetLoopPump(targetLoop);
        using var callerDriver = new ExecutorTestDriver(callerLoop);

        Exception? captured = null;

        callerDriver.Run(() =>
        {
            var task = OrientExecutor.InvokeAsync(
                targetLoop,
                () =>
                {
                    var source = new OrientTaskCompletionSource<OrientUnit>(targetLoop);
                    source.TrySetCanceled();
                    return new OrientTask(source.Task);
                });

            var awaiter = task.GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                try
                {
                    awaiter.GetResult();
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            });

            PumpCallerUntil(callerLoop, () => captured is not null, TimeSpan.FromSeconds(2));
        });

        Assert.IsType<TaskCanceledException>(captured);
    }

    private static async OrientTask<int> FaultingTargetAsync(Exception failure, OrientExecutor targetLoop)
    {
        var source = new OrientTaskCompletionSource<int>(targetLoop);
        source.TrySetException(failure);
        return await source.Task;
    }

    private static Exception? RunOnFreshThread(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                captured = exception;
            }
        });

        thread.Start();
        thread.Join();
        return captured;
    }

    private static void PumpCallerUntil(OrientExecutor callerLoop, Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            callerLoop.Tick();
            Thread.Sleep(1);
        }

        if (!condition())
        {
            throw new TimeoutException("Caller executor pump timed out.");
        }
    }

    private sealed class TargetLoopPump : IDisposable
    {
        private readonly OrientExecutor targetLoop;
        private readonly CancellationTokenSource cancellation = new();
        private readonly Thread thread;
        private volatile Exception? pumpFailure;

        public TargetLoopPump(OrientExecutor targetLoop)
        {
            this.targetLoop = targetLoop;
            thread = new Thread(PumpMain)
            {
                IsBackground = true,
                Name = "InvokeAsync target pump",
            };
            thread.Start();
        }

        public void EnsureNoFailure()
        {
            if (pumpFailure is not null)
            {
                throw new InvalidOperationException("Target executor pump failed.", pumpFailure);
            }
        }

        public void Dispose()
        {
            cancellation.Cancel();
            if (!thread.Join(TimeSpan.FromSeconds(2)))
            {
                throw new InvalidOperationException("Target executor pump thread did not exit.");
            }

            EnsureNoFailure();
        }

        private void PumpMain()
        {
            targetLoop.BindToCurrentThread();
            try
            {
                while (!cancellation.Token.IsCancellationRequested)
                {
                    targetLoop.Tick();
                    Thread.Sleep(1);
                }
            }
            catch (Exception exception)
            {
                pumpFailure = exception;
            }
            finally
            {
#if DEBUG
                OrientExecutor.ResetDebugThreadBindingForTests();
#endif
            }
        }
    }
}
