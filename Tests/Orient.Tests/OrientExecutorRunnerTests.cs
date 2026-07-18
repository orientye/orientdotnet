using Orient.Runtime;

namespace Orient.Tests;

public class OrientExecutorRunnerTests : OrientTestBase
{
    [Fact]
    public void RunUntilCompleteBindsLoopBeforeStartingOperation()
    {
        var executor = new OrientExecutor();
        OrientExecutor? capturedLoop = null;

        var result = OrientExecutorRunner.RunUntilComplete(
            executor,
            () =>
            {
                capturedLoop = OrientExecutor.Current;
                return OrientTask.FromResult(7, OrientExecutor.Current);
            });

        Assert.Same(executor, capturedLoop);
        Assert.Equal(7, result);
    }

    [Fact]
    public void RunUntilCompleteReturnsResultOnLoopThread()
    {
        var executor = new OrientExecutor();
        var callingThreadId = Environment.CurrentManagedThreadId;

        var continuationThreadId = OrientExecutorRunner.RunUntilComplete(
            executor,
            GetThreadIdAfterDelayAsync);

        Assert.Equal(callingThreadId, continuationThreadId);
    }

    [Fact]
    public void RunUntilCompleteVoidOverloadRunsOperation()
    {
        var executor = new OrientExecutor();
        var count = 0;

        OrientExecutorRunner.RunUntilComplete(
            executor,
            async () =>
            {
                await OrientTask.Delay(1, OrientExecutor.Current);
                count++;
            });

        Assert.Equal(1, count);
    }

    [Fact]
    public void RunUntilCompleteRethrowsOperationException()
    {
        var executor = new OrientExecutor();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            OrientExecutorRunner.RunUntilComplete(executor, ThrowAfterDelayAsync));

        Assert.Equal("runner failure", exception.Message);
    }

    private static async OrientTask<int> GetThreadIdAfterDelayAsync()
    {
        await OrientTask.Delay(1, OrientExecutor.Current);
        return Environment.CurrentManagedThreadId;
    }

    private static async OrientTask<int> ThrowAfterDelayAsync()
    {
        await OrientTask.Delay(1, OrientExecutor.Current);
        throw new InvalidOperationException("runner failure");
    }
}
