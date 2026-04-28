using CRpc.Async;
using CRpc.Rpc.CRpc.Server;

namespace CRPC.Tests;

public class CRpcLoopTests
{
    [Fact]
    public void TaskContinuationRunsOnLoopThreadWhenCompletedFromAnotherThread()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var loopThreadId = Environment.CurrentManagedThreadId;

        var source = new CRpcTaskCompletionSource<int>(loop);
        var continuationRan = false;
        int? continuationThreadId = null;

        var worker = new Thread(() =>
        {
            var awaiter = source.Task.GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                continuationRan = true;
                continuationThreadId = Environment.CurrentManagedThreadId;
            });

            Assert.True(source.TrySetResult(42));
        });

        worker.Start();
        worker.Join();

        Assert.False(continuationRan);

        loop.Tick();

        Assert.True(continuationRan);
        Assert.Equal(loopThreadId, continuationThreadId);
        Assert.Equal(42, source.Task.GetAwaiter().GetResult());
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

        var worker = new Thread(() => Assert.True(source.TrySetResult(41)));
        worker.Start();
        worker.Join();

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

        Assert.True(SpinWait.SpinUntil(() => task.GetAwaiter().IsCompleted, TimeSpan.FromSeconds(1)));
        Assert.Null(result);

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

        CRpcServerLoop.RunUntilCancelled(loop, cts.Token, sleepMilliseconds: 0);

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
}