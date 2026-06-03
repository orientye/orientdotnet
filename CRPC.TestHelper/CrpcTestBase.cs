using CRpc.Async;

namespace CRpc.TestHelper;

/// <summary>
/// Resets DEBUG per-thread CRpcLoop binding after each test so xUnit can reuse the test thread.
/// </summary>
public abstract class CrpcTestBase : IDisposable
{
    public void Dispose()
    {
#if DEBUG
        CRpcLoop.ResetDebugThreadBindingForTests();
#endif
    }
}
