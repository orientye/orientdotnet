using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Transport.Channels;

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
        return request.CreateResponse(code, body);
    }

    public static CRpcMessage BuildFrameworkErrorResponse(CRpcMessage request, CRpcStatusCode statusCode)
    {
        return BuildCrpcResponse(request, (int)statusCode, Array.Empty<byte>());
    }

    public static void WriteFrameworkErrorResponse(
        IChannelHandlerContext ctx,
        CRpcMessage request,
        CRpcStatusCode statusCode)
    {
        var response = BuildFrameworkErrorResponse(request, statusCode);
        ChannelWriteUtil.WriteAndFlushFireAndForget(ctx, response);
    }
}
