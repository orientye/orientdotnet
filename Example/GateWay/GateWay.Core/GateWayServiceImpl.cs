using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Codec;
using CRpc.Rpc.CRpc.Server;

namespace GateWay;

public sealed class GateWayServiceImpl : IRpcService
{
    private readonly GateWayRouter router;
    private readonly GateWayOptions options;

    public GateWayServiceImpl(GateWayRouter router, GateWayOptions options)
    {
        this.router = router ?? throw new ArgumentNullException(nameof(router));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ushort GetServiceId() => options.FallbackServiceId;

    public async CRpcTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
    {
        var msg = (CRpcMessage)req;
        var targetServiceId = msg.getServiceId();
        var targetMethodId = msg.getMethodId();
        var connection = ((CRpcContext)context).Connection;

        var link = await router.GetOrCreateLinkAsync(connection, targetServiceId);
        if (link is null)
        {
            return (-1, Array.Empty<byte>());
        }

        return await ForwardAsync(link, targetServiceId, targetMethodId, msg.getBody());
    }

    private async CRpcTask<(int, byte[])> ForwardAsync(
        GateWayBackendLink link,
        ushort serviceId,
        ushort methodId,
        byte[] body)
    {
        try
        {
            return await CallBackendAsync(link, serviceId, methodId, body);
        }
        catch (Exception exception) when (IsReconnectable(exception))
        {
            try
            {
                await link.ReconnectAsync();
                return await CallBackendAsync(link, serviceId, methodId, body);
            }
            catch
            {
                return (-1, Array.Empty<byte>());
            }
        }
        catch
        {
            return (-1, Array.Empty<byte>());
        }
    }

    private async CRpcTask<(int, byte[])> CallBackendAsync(
        GateWayBackendLink link,
        ushort serviceId,
        ushort methodId,
        byte[] body)
    {
        var response = await link.BackendClient.CallAsync(serviceId, methodId, body, options.DefaultTimeoutMs);
        return (response.getHeader().getResultCode(), response.getBody());
    }

    private static bool IsReconnectable(Exception exception)
    {
        return exception is InvalidOperationException invalidOperationException
            && invalidOperationException.Message.Contains("not connected", StringComparison.OrdinalIgnoreCase);
    }
}
