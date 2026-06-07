using CRpc.Async;
using CRpc.Rpc.CRpc.Client;

namespace GateWay;

public interface IBackendConnector
{
    CRpcTask ConnectAsync(CRpcClient client, GateWayOptions options);
}

public sealed class TcpBackendConnector : IBackendConnector
{
    public async CRpcTask ConnectAsync(CRpcClient client, GateWayOptions options)
    {
        await client.ConnectAsync(options.BackendHost, options.BackendPort);
    }
}
