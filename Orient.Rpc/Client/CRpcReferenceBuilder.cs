using Orient.Runtime;

namespace Orient.Rpc.Client;

public sealed class CRpcReferenceBuilder<TProxy>
    where TProxy : class, new()
{
    private Uri? uri;
    private CRpcClientOptions? clientOptions;

    public CRpcReferenceBuilder<TProxy> Url(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme != "crpc")
        {
            throw new InvalidOperationException("CRpc reference URL must use the crpc:// scheme.");
        }

        if (parsed.Port <= 0)
        {
            throw new InvalidOperationException("CRpc reference URL must include a port.");
        }

        uri = parsed;
        return this;
    }

    public CRpcReferenceBuilder<TProxy> ClientOptions(CRpcClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        clientOptions = options;
        return this;
    }

    /// <summary>
    /// Connects via <see cref="CRpcClient.ConnectAsync(string, int)"/> and returns a typed proxy.
    /// Must be called on the bound owner loop thread while the loop is driven.
    /// </summary>
    public OrientTask<CRpcReference<TProxy>> ConnectAsync(OrientLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        var target = uri ?? throw new InvalidOperationException("CRpc reference URL is required.");

        return ConnectAsyncCore(loop, target);
    }

    private async OrientTask<CRpcReference<TProxy>> ConnectAsyncCore(OrientLoop loop, Uri target)
    {
        var client = new CRpcClient(loop, clientOptions);
        await client.ConnectAsync(target.Host, target.Port);

        var proxy = CRpcProxyActivator.Create<TProxy>(client);
        return new CRpcReference<TProxy>(proxy, client);
    }
}
