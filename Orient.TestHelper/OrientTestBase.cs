using Orient.Runtime;

namespace Orient.TestHelper;

/// <summary>
/// Resets DEBUG per-thread OrientLoop binding after each test so xUnit can reuse the test thread.
/// </summary>
public abstract class OrientTestBase : IDisposable
{
    public void Dispose()
    {
#if DEBUG
        OrientLoop.ResetDebugThreadBindingForTests();
#endif
    }
}
