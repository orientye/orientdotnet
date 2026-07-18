using Orient.Runtime;

namespace Orient.Tests;

public class OrientExecutorThreadBindingTests : OrientTestBase
{
    [Fact]
    public void TickOnWrongThreadThrowsAfterBind()
    {
        var executor = new OrientExecutor();
        DedicatedExecutorThread.Run(executor, _ => { });

        var exception = Assert.Throws<InvalidOperationException>(() => executor.Tick());
        Assert.Contains("executor thread", exception.Message, StringComparison.Ordinal);
    }

#if DEBUG
    [Fact]
    public void BindSecondLoopOnSameThreadThrowsInDebug()
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                var firstLoop = new OrientExecutor();
                var secondLoop = new OrientExecutor();
                firstLoop.BindToCurrentThread();
                secondLoop.BindToCurrentThread();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        })
        {
            IsBackground = true,
        };

        thread.Start();
        thread.Join();

        var exception = Assert.IsType<InvalidOperationException>(captured);
        Assert.Contains("already bound", exception.Message, StringComparison.Ordinal);
    }
#endif
}
