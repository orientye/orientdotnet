using Orient.Rpc.Protocol;
using Orient.Rpc.Util;
using Orient.Rpc.Codec;
using DotNetty.Transport.Channels;

namespace GateWay;

internal static class GateWayResponseUtil
{
    public static void WriteResponse(IChannelHandlerContext ctx, CRpcMessage request, int resultCode, byte[] body)
    {
        var response = request.CreateResponse(resultCode, body);
        ChannelWriteUtil.WriteAndFlushFireAndForget(ctx, response);
    }

    public static void WriteErrorResponse(IChannelHandlerContext ctx, CRpcMessage request, int resultCode = (int)CRpcStatusCode.ServiceNotFound)
    {
        WriteResponse(ctx, request, resultCode, Array.Empty<byte>());
    }
}
