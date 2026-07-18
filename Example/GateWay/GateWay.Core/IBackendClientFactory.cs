using Orient.Runtime;
using Orient.Rpc.Client;

namespace GateWay;

public interface IBackendClientFactory
{
    CRpcClient Create(OrientExecutor executor);
}

public sealed class DefaultBackendClientFactory : IBackendClientFactory
{
    public CRpcClient Create(OrientExecutor executor) => new CRpcClient(executor);
}
