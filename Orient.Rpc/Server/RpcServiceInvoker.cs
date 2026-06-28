using Orient.Runtime;
using Orient.Rpc;
using Orient.Rpc.Protocol;
using Orient.Rpc.Util;
using Orient.Rpc.Codec;
using DotNetty.Transport.Channels;

namespace Orient.Rpc.Server;

internal static class RpcServiceInvoker
{
    public static async OrientTask<(int code, byte[] body)> InvokeAsync(
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
