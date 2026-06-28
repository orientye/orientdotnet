using Orient.Runtime;

namespace Orient.Rpc.Client;

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
    public OrientTask CloseAsync() => client.CloseAsync();

    /// <inheritdoc cref="CRpcClient.ShutdownIoAsync"/>
    public OrientTask ShutdownIoAsync() => client.ShutdownIoAsync();

    /// <inheritdoc cref="CRpcClient.DisposeAsync"/>
    public ValueTask DisposeAsync() => client.DisposeAsync();
}
