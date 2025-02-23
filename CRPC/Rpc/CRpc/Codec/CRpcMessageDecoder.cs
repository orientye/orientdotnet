using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Codec
{
    public class CRpcMessageDecoder : LengthFieldBasedFrameDecoder
    {
        private readonly int _hashLength;

        public CRpcMessageDecoder(int maxFrameLength, int hashLength):
            base(maxFrameLength, 4, 4, -8, 0)
        {
            _hashLength = hashLength;
        }

        protected override object Decode(IChannelHandlerContext context, IByteBuffer input)
        {
            if (input.ReadableBytes < 8)
            {
                return null;
            }
            
            IChannel channel = context.Channel;

            // extract frame & fast fail
            IByteBuffer frame = null;
            try
            {
                frame = (IByteBuffer)base.Decode(context, input);
                if (null == frame)
                {
                    Console.WriteLine("{0}数据包不完整...", channel);
                    return null;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("{0}解码异常, 断开连接: {1}", channel, e.Message);
                context.CloseAsync();
                return null;
            }
            
            int identity = frame.GetInt(frame.ReaderIndex);
            if (identity != CRpcMessage.MAGIC_NUM) {
                Console.WriteLine("{0}无效的数据包标识[{1}], 断开连接...", channel, identity);
                context.CloseAsync();
                ReferenceCountUtil.Release(frame);
                return null;
            }

            // frame to message
            try
            {
                return decodeMessage(context, frame);
            }
            catch (Exception e)
            {
                Console.WriteLine("{0}解码异常{1}, 断开连接...", channel, e);
                context.CloseAsync();
                return null;
            }
            finally
            {
                ReferenceCountUtil.Release(frame);
            }
        }

        private CRpcMessage decodeMessage(IChannelHandlerContext ctx, IByteBuffer frame)
        {
            // checksum
            CRpcMessage message = CRpcMessage.valueOf(frame);
            int checksum = frame.ReadInt();
            int bodyLength = message.getBody().Length;
            if (bodyLength > 0)
            {
                int hashsum = (int)ChecksumsUtil.BPHashPartly(message.getBody(), _hashLength);
                if (checksum != hashsum)
                {
                    Console.WriteLine("{0}消息校验码{1}与实际{2}不符, 断开连接...", ctx.Channel, checksum, hashsum);
                    ctx.CloseAsync();
                    throw new Exception("decodeMessage checksum failed");
                }
            }

            // compress state
            if (message.getHeader().hasState(CRpcMessageState.STATE_COMPRESS))
            {
                Console.WriteLine("{0}请求消息包含压缩状态", ctx.Channel);
                // return null;
            }
            return message;
        }
    }
}
