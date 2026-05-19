using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Codec;

namespace CRpc.Rpc.CRpc.Server;

internal static class RpcServiceInvoker
{
    public static async CRpcTask<(int code, byte[] body)> InvokeAsync(
        IRpcService service,
        CRpcContext context,
        CRpcMessage request)
    {
        var (code, body) = await service.OnMessageAsync(context, request);
        return (code, body);
    }

    public static CRpcMessage BuildCrpcResponse(CRpcMessage request, int code, byte[] body)
    {
        return request.createResponse(code, body);
    }
}
