using Orient.Runtime;
using Orient.Rpc.Client;

namespace GateWay;

public interface IBackendConnector
{
    OrientTask ConnectAsync(CRpcClient client, BackendEndpoint endpoint);
}

public sealed class TcpBackendConnector : IBackendConnector
{
    public async OrientTask ConnectAsync(CRpcClient client, BackendEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        await client.ConnectAsync(endpoint.Host, endpoint.Port);
    }
}
