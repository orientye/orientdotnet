using CRpc.Async;

namespace CRpc.Rpc.CRpc.Client;

public sealed class CRpcReferenceBuilder<TProxy>
    where TProxy : class, new()
{
    private Uri? uri;

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

    public async Task<CRpcReference<TProxy>> ConnectAsync(CRpcLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        var target = uri ?? throw new InvalidOperationException("CRpc reference URL is required.");

        var client = new CRpcClient(loop);
        await client.ConnectAsync(target.Host, target.Port).ConfigureAwait(false);

        var proxy = CRpcProxyActivator.Create<TProxy>(client);
        return new CRpcReference<TProxy>(proxy, client);
    }
}
