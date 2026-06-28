using Orient.Rpc;

namespace Orient.Rpc.Client;

public static class CRpcProxyActivator
{
    public static TProxy Create<TProxy>(IRpcClient rpcClient)
        where TProxy : class, new()
    {
        ArgumentNullException.ThrowIfNull(rpcClient);

        var proxy = new TProxy();
        if (proxy is not ICRpcGeneratedClient generatedClient)
        {
            throw new InvalidOperationException(
                $"{typeof(TProxy).FullName} must implement {nameof(ICRpcGeneratedClient)}.");
        }

        generatedClient.BindRpcClient(rpcClient);
        return proxy;
    }
}
