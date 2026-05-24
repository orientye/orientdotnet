using CRpc.Async;
using CRpc.Rpc.CRpc.Server;

namespace CRPC.Tests;

public class CRpcLoopTests : CrpcTestBase
{
    [Fact]
    public void FromResultWithoutLoopOrCurrentThrows()
    {
        var exception = RunOnFreshThread(() => CRpcTask.FromResult(1));
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("CRpcLoop", exception!.Message);
    }

    [Fact]
    public void RequireCurrentOrWithoutLoopOrCurrentThrows()
    {
        var exception = RunOnFreshThread(() => CRpcLoop.RequireCurrentOr());
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("CRpcLoop", exception!.Message);
    }

    [Fact]
    public void AwaitOnWrongThreadThrowsWithAwaitSpecificMessage()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();

        var source = new CRpcTaskCompletionSource<int>(loop);
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
        Assert.Contains("CRpcLoop", exception.Message);
    }

    [Fact]
    public void CompletionSourceRejectsCompletionFromNonLoopThread()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();

        var source = new CRpcTaskCompletionSource<int>(loop);
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

        Assert.Contains("loop thread", exception?.Message);
    }

    [Fact]
    public void AsyncCRpcTaskMethodResumesOnLoopThread()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var loopThreadId = Environment.CurrentManagedThreadId;

        var source = new CRpcTaskCompletionSource<int>(loop);
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

        loop.Tick();

        Assert.Equal(42, result);
        Assert.Equal(loopThreadId, continuationThreadId);
    }

    [Fact]
    public void FromTaskContinuationRunsOnLoopThread()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var loopThreadId = Environment.CurrentManagedThreadId;

        var dotNetSource = new TaskCompletionSource<int>();
        var task = CRpcTask.FromTask(dotNetSource.Task, loop);
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

        loop.Tick();

        Assert.Equal(7, result);
        Assert.Equal(loopThreadId, continuationThreadId);
    }

    [Fact]
    public void DelayContinuationRunsOnLoopThread()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var loopThreadId = Environment.CurrentManagedThreadId;

        var task = GetThreadIdAfterDelayAsync(loop);
        var awaiter = task.GetAwaiter();
        int? continuationThreadId = null;

        awaiter.OnCompleted(() => continuationThreadId = awaiter.GetResult());

        PumpUntil(loop, () => continuationThreadId is not null, TimeSpan.FromSeconds(2));

        Assert.Equal(loopThreadId, continuationThreadId);
    }

    [Fact]
    public void DelayThrowsWhenExplicitLoopFromNonLoopThread()
    {
        var loop = new CRpcLoop();
        var exception = RunOnFreshThread(() => CRpcTask.Delay(1, loop));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("loop thread", exception!.Message);
    }

    [Fact]
    public void DelayZeroThrowsWhenExplicitLoopFromNonLoopThread()
    {
        var loop = new CRpcLoop();
        var exception = RunOnFreshThread(() => CRpcTask.Delay(0, loop));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("loop thread", exception!.Message);
    }

    [Fact]
    public void DelayDoesNotCompleteUntilLoopTicksExpiredTimer()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();

        var task = CRpcTask.Delay(1, loop);
        var awaiter = task.GetAwaiter();

        Thread.Sleep(20);

        Assert.False(awaiter.IsCompleted);

        loop.Tick();

        Assert.True(awaiter.IsCompleted);
    }

    [Fact]
    public void ServerLoopPumpsUntilCancellationWithoutReadingConsoleKeys()
    {
        var loop = new CRpcLoop();
        using var cts = new CancellationTokenSource();
        var pumped = false;

        loop.Post(() =>
        {
            pumped = true;
            cts.Cancel();
        });

        CRpcServerLoop.RunUntilCancelled(loop, cts.Token);

        Assert.True(pumped);
    }

    private static async CRpcTask<int> AddOneAsync(CRpcTask<int> valueTask)
    {
        var value = await valueTask;
        return value + 1;
    }

    private static async CRpcTask<int> GetThreadIdAfterDelayAsync(CRpcLoop loop)
    {
        await CRpcTask.Delay(1, loop);
        return Environment.CurrentManagedThreadId;
    }

    private static void PumpUntil(CRpcLoop loop, Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            loop.Tick();
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