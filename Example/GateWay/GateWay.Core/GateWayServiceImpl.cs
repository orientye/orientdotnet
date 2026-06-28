using Orient.Runtime;
using Orient.Rpc;
using Orient.Rpc.Codec;
using Orient.Rpc.Server;

namespace GateWay;

public sealed class GateWayServiceImpl : IRpcService
{
    private readonly GateWayRouter router;

    public GateWayServiceImpl(GateWayRouter router)
    {
        this.router = router ?? throw new ArgumentNullException(nameof(router));
    }

    public ushort GetServiceId() => router.Config.FallbackServiceId;

    public async OrientTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
    {
        var msg = (CRpcMessage)req;
        var targetServiceId = msg.ServiceId;
        var targetMethodId = msg.MethodId;
        var connection = ((CRpcContext)context).Connection;

        var link = await router.GetOrCreateLinkAsync(connection, targetServiceId);
        if (link is null)
        {
            return (-1, Array.Empty<byte>());
        }

        return await ForwardAsync(link, targetServiceId, targetMethodId, msg.Body);
    }

    private async OrientTask<(int, byte[])> ForwardAsync(
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
            MarkEndpointUnhealthy(link);
            try
            {
                await link.ReconnectAsync();
                var result = await CallBackendAsync(link, serviceId, methodId, body);
                MarkEndpointHealthy(link);
                return result;
            }
            catch
            {
                MarkEndpointUnhealthy(link);
                return (-1, Array.Empty<byte>());
            }
        }
        catch
        {
            MarkEndpointUnhealthy(link);
            return (-1, Array.Empty<byte>());
        }
    }

    private async OrientTask<(int, byte[])> CallBackendAsync(
        GateWayBackendLink link,
        ushort serviceId,
        ushort methodId,
        byte[] body)
    {
        var response = await link.BackendClient.CallAsync(
            serviceId,
            methodId,
            body,
            router.Config.DefaultTimeoutMs);
        return (response.ResultCode, response.Body);
    }

    private void MarkEndpointUnhealthy(GateWayBackendLink link)
    {
        if (router.PoolRegistry.TryGetPool(link.ServiceId, out var pool))
        {
            pool.MarkUnhealthy(link.Endpoint);
        }
    }

    private void MarkEndpointHealthy(GateWayBackendLink link)
    {
        if (router.PoolRegistry.TryGetPool(link.ServiceId, out var pool))
        {
            pool.MarkHealthy(link.Endpoint);
        }
    }

    private static bool IsReconnectable(Exception exception)
    {
        return exception is InvalidOperationException invalidOperationException
            && invalidOperationException.Message.Contains("not connected", StringComparison.OrdinalIgnoreCase);
    }
}
