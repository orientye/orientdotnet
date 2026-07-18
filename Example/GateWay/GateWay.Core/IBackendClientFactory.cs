using Orient.Runtime;
using Orient.Rpc.Client;

namespace GateWay;

public interface IBackendClientFactory
{
    CRpcClient Create(OrientExecutor loop);
}

public sealed class DefaultBackendClientFactory : IBackendClientFactory
{
    public CRpcClient Create(OrientExecutor loop) => new CRpcClient(loop);
}
