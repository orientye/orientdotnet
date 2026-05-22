using CRpc.Async;

namespace CRPC.Tests;

public class CRpcLoopRunnerTests : CrpcTestBase
{
    [Fact]
    public void RunUntilCompleteBindsLoopBeforeStartingOperation()
    {
        var loop = new CRpcLoop();
        CRpcLoop? capturedLoop = null;

        var result = CRpcLoopRunner.RunUntilComplete(
            loop,
            () =>
            {
                capturedLoop = CRpcLoop.Current;
                return CRpcTask.FromResult(7, CRpcLoop.Current);
            });

        Assert.Same(loop, capturedLoop);
        Assert.Equal(7, result);
    }

    [Fact]
    public void RunUntilCompleteReturnsResultOnLoopThread()
    {
        var loop = new CRpcLoop();
        var callingThreadId = Environment.CurrentManagedThreadId;

        var continuationThreadId = CRpcLoopRunner.RunUntilComplete(
            loop,
            GetThreadIdAfterDelayAsync);

        Assert.Equal(callingThreadId, continuationThreadId);
    }

    [Fact]
    public void RunUntilCompleteVoidOverloadRunsOperation()
    {
        var loop = new CRpcLoop();
        var count = 0;

        CRpcLoopRunner.RunUntilComplete(
            loop,
            async () =>
            {
                await CRpcTask.Delay(1, CRpcLoop.Current);
                count++;
            });

        Assert.Equal(1, count);
    }

    [Fact]
    public void RunUntilCompleteRethrowsOperationException()
    {
        var loop = new CRpcLoop();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CRpcLoopRunner.RunUntilComplete(loop, ThrowAfterDelayAsync));

        Assert.Equal("runner failure", exception.Message);
    }

    private static async CRpcTask<int> GetThreadIdAfterDelayAsync()
    {
        await CRpcTask.Delay(1, CRpcLoop.Current);
        return Environment.CurrentManagedThreadId;
    }

    private static async CRpcTask<int> ThrowAfterDelayAsync()
    {
        await CRpcTask.Delay(1, CRpcLoop.Current);
        throw new InvalidOperationException("runner failure");
    }
}
