using Orient.Runtime;

namespace Orient.Rpc.Client;

public sealed class CRpcPushContext
{
    public CRpcPushContext(OrientExecutor executor, ushort serviceId, ushort methodId)
    {
        Executor = executor ?? throw new ArgumentNullException(nameof(executor));
        ServiceId = serviceId;
        MethodId = methodId;
    }

    public OrientExecutor Executor { get; }

    public ushort ServiceId { get; }

    public ushort MethodId { get; }
}
