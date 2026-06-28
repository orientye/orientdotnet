using Orient.Runtime;
using Orient.Rpc.Client;

namespace GateWay;

public interface IBackendClientFactory
{
    CRpcClient Create(OrientLoop loop);
}

public sealed class DefaultBackendClientFactory : IBackendClientFactory
{
    public CRpcClient Create(OrientLoop loop) => new CRpcClient(loop);
}
