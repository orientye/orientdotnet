using CRpc.Rpc.CRpc;
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Transport.Channels;

namespace GateWay;

internal static class GateWayResponseUtil
{
    public static void WriteResponse(IChannelHandlerContext ctx, CRpcMessage request, int resultCode, byte[] body)
    {
        var response = request.createResponse(resultCode, body);
        ChannelWriteUtil.WriteAndFlushFireAndForget(ctx, response);
    }

    public static void WriteErrorResponse(IChannelHandlerContext ctx, CRpcMessage request, int resultCode = -1)
    {
        WriteResponse(ctx, request, resultCode, Array.Empty<byte>());
    }
}
