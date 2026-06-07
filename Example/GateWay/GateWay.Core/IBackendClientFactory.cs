using CRpc.Async;
using CRpc.Rpc.CRpc.Client;

namespace GateWay;

public interface IBackendClientFactory
{
    CRpcClient Create(CRpcLoop loop);
}

public sealed class DefaultBackendClientFactory : IBackendClientFactory
{
    public CRpcClient Create(CRpcLoop loop) => new CRpcClient(loop);
}
