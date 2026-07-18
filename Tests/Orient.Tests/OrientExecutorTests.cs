using Orient.Runtime;
using Orient.Rpc.Server;

namespace Orient.Tests;

public class OrientExecutorTests : OrientTestBase
{
    [Fact]
    public void FromResultWithoutLoopOrCurrentThrows()
    {
        var exception = RunOnFreshThread(() => OrientTask.FromResult(1));
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("OrientExecutor", exception!.Message);
    }

    [Fact]
    public void RequireCurrentOrWithoutLoopOrCurrentThrows()
    {
        var exception = RunOnFreshThread(() => OrientExecutor.RequireCurrentOr());
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("OrientExecutor", exception!.Message);
    }

    [Fact]
    public void AwaitOnWrongThreadThrowsWithAwaitSpecificMessage()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        var source = new OrientTaskCompletionSource<int>(executor);
        source.TrySetResult(42);
        var task = source.Task;

        Exception? exception = null;
        var isCompletedOnWrongThread = false;

        var worker = new Thread(() =>
        {
            try
            {
                var awaiter = task.GetAwaiter();
                isCompletedOnWrongThread = awaiter.IsCompleted;
                awaiter.OnCompleted(() => { });
            }
            catch (Exception caughtException)
            {
                exception = caughtException;
            }
        });

        worker.Start();
        worker.Join();

        Assert.False(isCompletedOnWrongThread);
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("awaited", exception!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OrientExecutor", exception.Message);
    }

    [Fact]
    public void CompletionSourceRejectsCompletionFromNonLoopThread()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        var source = new OrientTaskCompletionSource<int>(executor);
        Exception? exception = null;

        var worker = new Thread(() =>
        {
            try
            {
                source.TrySetResult(42);
            }
            catch (Exception caughtException)
            {
                exception = caughtException;
            }
        });

        worker.Start();
        worker.Join();

        Assert.Contains("executor thread", exception?.Message);
    }

    [Fact]
    public void AsyncOrientTaskMethodResumesOnLoopThread()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var executorThreadId = Environment.CurrentManagedThreadId;

        var source = new OrientTaskCompletionSource<int>(executor);
        var task = AddOneAsync(source.Task);
        int? result = null;
        int? continuationThreadId = null;

        var awaiter = task.GetAwaiter();
        awaiter.OnCompleted(() =>
        {
            result = awaiter.GetResult();
            continuationThreadId = Environment.CurrentManagedThreadId;
        });

        Assert.True(source.TrySetResult(41));

        Assert.Null(result);

        executor.Tick();

        Assert.Equal(42, result);
        Assert.Equal(executorThreadId, continuationThreadId);
    }

    [Fact]
    public void FromTaskContinuationRunsOnLoopThread()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var executorThreadId = Environment.CurrentManagedThreadId;

        var dotNetSource = new TaskCompletionSource<int>();
        var task = OrientTask.FromTask(dotNetSource.Task, executor);
        var awaiter = task.GetAwaiter();
        int? result = null;
        int? continuationThreadId = null;

        awaiter.OnCompleted(() =>
        {
            result = awaiter.GetResult();
            continuationThreadId = Environment.CurrentManagedThreadId;
        });

        var worker = new Thread(() => dotNetSource.SetResult(7));
        worker.Start();
        worker.Join();

        Assert.Null(result);
        Assert.False(awaiter.IsCompleted);

        executor.Tick();

        Assert.Equal(7, result);
        Assert.Equal(executorThreadId, continuationThreadId);
    }

    [Fact]
    public void DelayContinuationRunsOnLoopThread()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var executorThreadId = Environment.CurrentManagedThreadId;

        var task = GetThreadIdAfterDelayAsync(executor);
        var awaiter = task.GetAwaiter();
        int? continuationThreadId = null;

        awaiter.OnCompleted(() => continuationThreadId = awaiter.GetResult());

        PumpUntil(executor, () => continuationThreadId is not null, TimeSpan.FromSeconds(2));

        Assert.Equal(executorThreadId, continuationThreadId);
    }

    [Fact]
    public void DelayThrowsWhenExplicitLoopFromNonLoopThread()
    {
        var executor = new OrientExecutor();
        var exception = RunOnFreshThread(() => OrientTask.Delay(1, executor));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("executor thread", exception!.Message);
    }

    [Fact]
    public void DelayZeroThrowsWhenExplicitLoopFromNonLoopThread()
    {
        var executor = new OrientExecutor();
        var exception = RunOnFreshThread(() => OrientTask.Delay(0, executor));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("executor thread", exception!.Message);
    }

    [Fact]
    public void DelayDoesNotCompleteUntilLoopTicksExpiredTimer()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        var task = OrientTask.Delay(1, executor);
        var awaiter = task.GetAwaiter();

        Thread.Sleep(20);

        Assert.False(awaiter.IsCompleted);

        executor.Tick();

        Assert.True(awaiter.IsCompleted);
    }

    [Fact]
    public void ServerLoopPumpsUntilCancellationWithoutReadingConsoleKeys()
    {
        var executor = new OrientExecutor();
        using var cts = new CancellationTokenSource();
        var pumped = false;

        executor.Post(() =>
        {
            pumped = true;
            cts.Cancel();
        });

        OrientExecutorHost.RunUntilCancelled(executor, cts.Token);

        Assert.True(pumped);
    }

    private static async OrientTask<int> AddOneAsync(OrientTask<int> valueTask)
    {
        var value = await valueTask;
        return value + 1;
    }

    private static async OrientTask<int> GetThreadIdAfterDelayAsync(OrientExecutor executor)
    {
        await OrientTask.Delay(1, executor);
        return Environment.CurrentManagedThreadId;
    }

    private static void PumpUntil(OrientExecutor executor, Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            executor.Tick();
            Thread.Sleep(1);
        }
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
}