using Orient.Runtime;

namespace CRPC.Tests;

public class OrientLoopRunnerTests : CrpcTestBase
{
    [Fact]
    public void RunUntilCompleteBindsLoopBeforeStartingOperation()
    {
        var loop = new OrientLoop();
        OrientLoop? capturedLoop = null;

        var result = OrientLoopRunner.RunUntilComplete(
            loop,
            () =>
            {
                capturedLoop = OrientLoop.Current;
                return OrientTask.FromResult(7, OrientLoop.Current);
            });

        Assert.Same(loop, capturedLoop);
        Assert.Equal(7, result);
    }

    [Fact]
    public void RunUntilCompleteReturnsResultOnLoopThread()
    {
        var loop = new OrientLoop();
        var callingThreadId = Environment.CurrentManagedThreadId;

        var continuationThreadId = OrientLoopRunner.RunUntilComplete(
            loop,
            GetThreadIdAfterDelayAsync);

        Assert.Equal(callingThreadId, continuationThreadId);
    }

    [Fact]
    public void RunUntilCompleteVoidOverloadRunsOperation()
    {
        var loop = new OrientLoop();
        var count = 0;

        OrientLoopRunner.RunUntilComplete(
            loop,
            async () =>
            {
                await OrientTask.Delay(1, OrientLoop.Current);
                count++;
            });

        Assert.Equal(1, count);
    }

    [Fact]
    public void RunUntilCompleteRethrowsOperationException()
    {
        var loop = new OrientLoop();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            OrientLoopRunner.RunUntilComplete(loop, ThrowAfterDelayAsync));

        Assert.Equal("runner failure", exception.Message);
    }

    private static async OrientTask<int> GetThreadIdAfterDelayAsync()
    {
        await OrientTask.Delay(1, OrientLoop.Current);
        return Environment.CurrentManagedThreadId;
    }

    private static async OrientTask<int> ThrowAfterDelayAsync()
    {
        await OrientTask.Delay(1, OrientLoop.Current);
        throw new InvalidOperationException("runner failure");
    }
}
