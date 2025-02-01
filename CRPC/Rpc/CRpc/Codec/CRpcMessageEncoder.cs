using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;

namespace CRPC.Rpc.CRpc.Codec
{
    public class CRpcMessageEncoder : MessageToByteEncoder<Object>
    {
        protected override void Encode(IChannelHandlerContext context, Object message, IByteBuffer output) 
        {

        }
    }
}
