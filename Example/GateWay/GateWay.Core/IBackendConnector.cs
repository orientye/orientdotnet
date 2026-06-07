using CRpc.Async;
using CRpc.Rpc.CRpc.Client;

namespace GateWay;

public interface IBackendConnector
{
    CRpcTask ConnectAsync(CRpcClient client, BackendEndpoint endpoint);
}

public sealed class TcpBackendConnector : IBackendConnector
{
    public async CRpcTask ConnectAsync(CRpcClient client, BackendEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        await client.ConnectAsync(endpoint.Host, endpoint.Port);
    }
}
