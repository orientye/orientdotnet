using CRpc.Async;

namespace LordUnion.IntegrationTests.Tests;

/// <summary>
/// Resets DEBUG per-thread CRpcLoop binding after each test so xUnit can reuse the test thread.
/// </summary>
public abstract class CrpcTestBase : IDisposable
{
    public void Dispose() => CRpcLoop.ResetDebugThreadBindingForTests();
}
