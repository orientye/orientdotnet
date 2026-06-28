using Orient.Runtime;

namespace Orient.Tests;

public class OrientLoopThreadBindingTests : OrientTestBase
{
    [Fact]
    public void TickOnWrongThreadThrowsAfterBind()
    {
        var loop = new OrientLoop();
        DedicatedLoopThread.Run(loop, _ => { });

        var exception = Assert.Throws<InvalidOperationException>(() => loop.Tick());
        Assert.Contains("loop thread", exception.Message, StringComparison.Ordinal);
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
                var firstLoop = new OrientLoop();
                var secondLoop = new OrientLoop();
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
