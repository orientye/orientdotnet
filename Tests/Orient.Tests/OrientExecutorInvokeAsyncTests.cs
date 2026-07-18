using Orient.Runtime;

namespace Orient.Tests;

public class OrientExecutorInvokeAsyncTests : OrientTestBase
{
    [Fact]
    public void InvokeAsyncWithoutCurrentLoopThrowsRequireCurrentOrMessage()
    {
        var targetExecutor = new OrientExecutor();
        var exception = RunOnFreshThread(() =>
            OrientExecutor.InvokeAsync(targetExecutor, () => 1));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("OrientExecutor must be provided", exception!.Message);
    }

    [Fact]
    public void InvokeAsyncNullTargetExecutorThrows()
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
    public void InvokeAsyncSyncValueRunsOnTargetExecutorAndCompletesOnCallerExecutor()
    {
        var callerExecutor = new OrientExecutor();
        var targetExecutor = new OrientExecutor();
        using var targetPump = new TargetExecutorPump(targetExecutor);
        using var callerDriver = new ExecutorTestDriver(callerExecutor);

        int? actionThreadId = null;
        int? continuationThreadId = null;
        int? result = null;

        callerDriver.Run(() =>
        {
            var callerThreadId = Environment.CurrentManagedThreadId;
            var task = OrientExecutor.InvokeAsync(
                targetExecutor,
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

            PumpCallerUntil(callerExecutor, () => result is not null, TimeSpan.FromSeconds(2));

            Assert.Equal(callerThreadId, continuationThreadId);
            Assert.NotEqual(callerThreadId, actionThreadId);
        });

        targetPump.EnsureNoFailure();
        Assert.Equal(42, result);
    }

    [Fact]
    public void InvokeAsyncSyncVoidRunsOnTargetExecutorAndCompletesOnCallerExecutor()
    {
        var callerExecutor = new OrientExecutor();
        var targetExecutor = new OrientExecutor();
        using var targetPump = new TargetExecutorPump(targetExecutor);
        using var callerDriver = new ExecutorTestDriver(callerExecutor);

        int? actionThreadId = null;
        var completed = false;

        callerDriver.Run(() =>
        {
            var callerThreadId = Environment.CurrentManagedThreadId;
            var task = OrientExecutor.InvokeAsync(
                targetExecutor,
                () => actionThreadId = Environment.CurrentManagedThreadId);

            var awaiter = task.GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                awaiter.GetResult();
                completed = true;
            });

            PumpCallerUntil(callerExecutor, () => completed, TimeSpan.FromSeconds(2));
            Assert.NotEqual(callerThreadId, actionThreadId);
        });

        Assert.True(completed);
    }

    [Fact]
    public void InvokeAsyncAsyncValueRunsOnTargetExecutorAndResumesOnCallerExecutor()
    {
        var callerExecutor = new OrientExecutor();
        var targetExecutor = new OrientExecutor();
        using var targetPump = new TargetExecutorPump(targetExecutor);
        using var callerDriver = new ExecutorTestDriver(callerExecutor);

        int? actionThreadId = null;
        int? continuationThreadId = null;
        int? result = null;

        callerDriver.Run(() =>
        {
            var callerThreadId = Environment.CurrentManagedThreadId;
            var task = OrientExecutor.InvokeAsync(
                targetExecutor,
                async () =>
                {
                    actionThreadId = Environment.CurrentManagedThreadId;
                    await OrientTask.Delay(1, targetExecutor);
                    return 7;
                });

            var awaiter = task.GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                continuationThreadId = Environment.CurrentManagedThreadId;
                result = awaiter.GetResult();
            });

            PumpCallerUntil(callerExecutor, () => result is not null, TimeSpan.FromSeconds(2));
            Assert.Equal(callerThreadId, continuationThreadId);
            Assert.NotEqual(callerThreadId, actionThreadId);
        });

        Assert.Equal(7, result);
    }

    [Fact]
    public void InvokeAsyncAsyncVoidRunsOnTargetExecutorAndCompletesOnCallerExecutor()
    {
        var callerExecutor = new OrientExecutor();
        var targetExecutor = new OrientExecutor();
        using var targetPump = new TargetExecutorPump(targetExecutor);
        using var callerDriver = new ExecutorTestDriver(callerExecutor);

        int? actionThreadId = null;
        var completed = false;

        callerDriver.Run(() =>
        {
            var callerThreadId = Environment.CurrentManagedThreadId;
            var task = OrientExecutor.InvokeAsync(
                targetExecutor,
                async () =>
                {
                    actionThreadId = Environment.CurrentManagedThreadId;
                    await OrientTask.Delay(1, targetExecutor);
                });

            var awaiter = task.GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                awaiter.GetResult();
                completed = true;
            });

            PumpCallerUntil(callerExecutor, () => completed, TimeSpan.FromSeconds(2));
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
        var callerExecutor = new OrientExecutor();
        var targetExecutor = new OrientExecutor();
        using var targetPump = new TargetExecutorPump(targetExecutor);
        using var callerDriver = new ExecutorTestDriver(callerExecutor);

        Exception? captured = null;

        callerDriver.Run(() =>
        {
            var failure = new InvalidOperationException("sync boom");
            var task = OrientExecutor.InvokeAsync<int>(
                targetExecutor,
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

            PumpCallerUntil(callerExecutor, () => captured is not null, TimeSpan.FromSeconds(2));
        });

        Assert.IsType<InvalidOperationException>(captured);
        Assert.Equal("sync boom", captured!.Message);
    }

    [Fact]
    public void InvokeAsyncAsyncTargetTaskExceptionFaultsCallerTask()
    {
        var callerExecutor = new OrientExecutor();
        var targetExecutor = new OrientExecutor();
        using var targetPump = new TargetExecutorPump(targetExecutor);
        using var callerDriver = new ExecutorTestDriver(callerExecutor);

        Exception? captured = null;

        callerDriver.Run(() =>
        {
            var failure = new InvalidOperationException("async boom");
            var task = OrientExecutor.InvokeAsync(
                targetExecutor,
                () => FaultingTargetAsync(failure, targetExecutor));

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

            PumpCallerUntil(callerExecutor, () => captured is not null, TimeSpan.FromSeconds(2));
        });

        Assert.IsType<InvalidOperationException>(captured);
        Assert.Equal("async boom", captured!.Message);
    }

    [Fact]
    public void InvokeAsyncTargetRunnerExceptionFaultsCallerTaskInsteadOfUnhandledException()
    {
        var callerExecutor = new OrientExecutor();
        var targetExecutor = new OrientExecutor();
        using var targetPump = new TargetExecutorPump(targetExecutor);
        using var callerDriver = new ExecutorTestDriver(callerExecutor);

        Exception? unhandled = null;
        Exception? captured = null;

        targetExecutor.UnhandledException += ex => unhandled = ex;

        callerDriver.Run(() =>
        {
            var failure = new InvalidOperationException("runner boom");
            var task = OrientExecutor.InvokeAsync(
                targetExecutor,
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

            PumpCallerUntil(callerExecutor, () => captured is not null, TimeSpan.FromSeconds(2));
        });

        Assert.Null(unhandled);
        Assert.Equal("runner boom", captured!.Message);
    }

    [Fact]
    public void InvokeAsyncCanceledTargetTaskCancelsCallerTask()
    {
        var callerExecutor = new OrientExecutor();
        var targetExecutor = new OrientExecutor();
        using var targetPump = new TargetExecutorPump(targetExecutor);
        using var callerDriver = new ExecutorTestDriver(callerExecutor);

        Exception? captured = null;

        callerDriver.Run(() =>
        {
            var task = OrientExecutor.InvokeAsync(
                targetExecutor,
                () =>
                {
                    var source = new OrientTaskCompletionSource<OrientUnit>(targetExecutor);
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

            PumpCallerUntil(callerExecutor, () => captured is not null, TimeSpan.FromSeconds(2));
        });

        Assert.IsType<TaskCanceledException>(captured);
    }

    private static async OrientTask<int> FaultingTargetAsync(Exception failure, OrientExecutor targetExecutor)
    {
        var source = new OrientTaskCompletionSource<int>(targetExecutor);
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

    private static void PumpCallerUntil(OrientExecutor callerExecutor, Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            callerExecutor.Tick();
            Thread.Sleep(1);
        }

        if (!condition())
        {
            throw new TimeoutException("Caller executor pump timed out.");
        }
    }

    private sealed class TargetExecutorPump : IDisposable
    {
        private readonly OrientExecutor targetExecutor;
        private readonly CancellationTokenSource cancellation = new();
        private readonly Thread thread;
        private volatile Exception? pumpFailure;

        public TargetExecutorPump(OrientExecutor targetExecutor)
        {
            this.targetExecutor = targetExecutor;
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
            targetExecutor.BindToCurrentThread();
            try
            {
                while (!cancellation.Token.IsCancellationRequested)
                {
                    targetExecutor.Tick();
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
