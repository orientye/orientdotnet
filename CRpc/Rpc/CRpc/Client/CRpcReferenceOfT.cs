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

    /// <inheritdoc cref="CRpcClient.CloseAsync"/>
    public CRpcTask CloseAsync() => client.CloseAsync();

    /// <inheritdoc cref="CRpcClient.ShutdownIoAsync"/>
    public CRpcTask ShutdownIoAsync() => client.ShutdownIoAsync();

    /// <inheritdoc cref="CRpcClient.DisposeAsync"/>
    public ValueTask DisposeAsync() => client.DisposeAsync();
}
