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

    public async ValueTask DisposeAsync()
    {
        await client.DisposeAsync();
    }
}
