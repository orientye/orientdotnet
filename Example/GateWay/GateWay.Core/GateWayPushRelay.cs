using CRpc.Async;
using CRpc.Rpc.CRpc.Client;

namespace GateWay;

public sealed class GateWayPushRelay
{
    // HelloWorld demo: Greeter ServerNotice push (serviceId=1000, methodId=2).
    private const ushort HelloWorldServiceId = 1000;
    private const ushort HelloWorldServerNoticeMethodId = 2;

    public void Attach(GateWayBackendLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        link.BackendClient.RegisterPushHandler(
            HelloWorldServiceId,
            HelloWorldServerNoticeMethodId,
            (context, body) => ForwardPushAsync(link, body));
    }

    private static async CRpcTask ForwardPushAsync(GateWayBackendLink link, byte[] body)
    {
        await link.Inbound.SendPushAsync(HelloWorldServiceId, HelloWorldServerNoticeMethodId, body);
    }
}
