using Orient.Logging;
using Orient.Runtime;
using Orient.Rpc.Client;

namespace GateWay;

public interface IBackendClientFactory
{
    CRpcClient Create(OrientExecutor executor);
}

public sealed class DefaultBackendClientFactory : IBackendClientFactory
{
    private readonly IOrientLoggerFactory loggerFactory;

    public DefaultBackendClientFactory(IOrientLoggerFactory? loggerFactory = null)
    {
        this.loggerFactory = loggerFactory ?? NullOrientLoggerFactory.Instance;
    }

    public CRpcClient Create(OrientExecutor executor) =>
        new(executor, new CRpcClientOptions { LoggerFactory = loggerFactory });
}
