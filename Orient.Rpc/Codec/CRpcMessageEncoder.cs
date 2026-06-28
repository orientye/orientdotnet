using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;

namespace Orient.Rpc.Codec;

public sealed class CRpcMessageEncoder : MessageToByteEncoder<CRpcMessage>
{
    protected override void Encode(IChannelHandlerContext context, CRpcMessage message, IByteBuffer output)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.WriteTo(output);
    }
}
