using Orient.Runtime;

namespace Orient.Rpc.Client;

public sealed class CRpcPushContext
{
    public CRpcPushContext(OrientLoop loop, ushort serviceId, ushort methodId)
    {
        Loop = loop ?? throw new ArgumentNullException(nameof(loop));
        ServiceId = serviceId;
        MethodId = methodId;
    }

    public OrientLoop Loop { get; }

    public ushort ServiceId { get; }

    public ushort MethodId { get; }
}
