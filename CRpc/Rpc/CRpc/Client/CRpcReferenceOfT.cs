using CRpc.Async;

namespace CRpc.Rpc.CRpc.Client;

public sealed class CRpcReference<TProxy> : IAsyncDisposable
    where TProxy : class
{
    private readonly CRpcClient client;

    internal CRpcReference(TProxy proxy, CRpcClient client)
    {
        Proxy = proxy;
        this.client = client;
    }

    public TProxy Proxy { get; }

    public CRpcTask CloseAsync() => client.CloseAsync();

    public CRpcTask ShutdownIoAsync() => client.ShutdownIoAsync();

    public ValueTask DisposeAsync() => client.DisposeAsync();
}
