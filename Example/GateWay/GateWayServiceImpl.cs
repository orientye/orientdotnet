using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Client;
using CRpc.Rpc.CRpc.Codec;
using CRpc.Rpc.CRpc.Server;

namespace GateWay;

public sealed class GateWayServiceImpl : IRpcService
{
    private readonly GateWayRouter router;

    public GateWayServiceImpl(GateWayRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        this.router = router;
    }

    public ushort GetServiceId() => 0;

    public async CRpcTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
    {
        var msg = (CRpcMessage)req;
        var targetServiceId = msg.getServiceId();
        var targetMethodId = msg.getMethodId();

        Console.WriteLine($"GateWay forwarding: serviceId={targetServiceId}, methodId={targetMethodId}");

        var backend = router.GetBackend(targetServiceId);
        if (backend is null)
        {
            Console.WriteLine($"GateWay no backend found for serviceId={targetServiceId}");
            return (-1, Array.Empty<byte>());
        }

        try
        {
            var response = await backend.CallAsync(targetServiceId, targetMethodId, msg.getBody(), 5000);
            return (response.getHeader().getResultCode(), response.getBody());
        }
        catch (Exception exception)
        {
            Console.WriteLine($"GateWay backend call failed: {exception}");
            return (-1, Array.Empty<byte>());
        }
    }
}
