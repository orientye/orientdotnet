namespace Orient.Rpc.Server;

public sealed class CRpcContext : IRpcContext
{
    public CRpcContext(CRpcConnection connection)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public CRpcConnection Connection { get; }
}
