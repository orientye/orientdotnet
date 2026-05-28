using CRpc.Async;

namespace CRpc.Rpc.CRpc.Client;

public sealed class CRpcPushContext
{
    public CRpcPushContext(CRpcLoop loop, ushort serviceId, ushort methodId)
    {
        Loop = loop ?? throw new ArgumentNullException(nameof(loop));
        ServiceId = serviceId;
        MethodId = methodId;
    }

    public CRpcLoop Loop { get; }

    public ushort ServiceId { get; }

    public ushort MethodId { get; }
}
