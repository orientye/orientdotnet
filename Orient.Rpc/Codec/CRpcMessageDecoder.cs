using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;

namespace Orient.Rpc.Codec;

public sealed class CRpcMessageDecoder : LengthFieldBasedFrameDecoder
{
    public CRpcMessageDecoder(int maxFrameLength)
        : base(maxFrameLength, lengthFieldOffset: 4, lengthFieldLength: 4, lengthAdjustment: 0, initialBytesToStrip: 8)
    {
    }

    protected override object Decode(IChannelHandlerContext context, IByteBuffer input)
    {
        if (input.ReadableBytes < CRpcMessage.FramePrefixLength)
        {
            return null;
        }

        if (input.GetInt(input.ReaderIndex) != CRpcMessage.Magic)
        {
            Console.WriteLine(
                $"{context.Channel} CRpc decode failed, closing connection: invalid magic 0x{input.GetInt(input.ReaderIndex):X8}.");
            _ = context.CloseAsync();
            return null;
        }

        IByteBuffer frame = null;
        try
        {
            frame = (IByteBuffer)base.Decode(context, input);
            if (frame is null)
            {
                return null;
            }

            return CRpcMessage.ReadFromPayload(frame, frame.ReadableBytes);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"{context.Channel} CRpc decode failed, closing connection: {exception.Message}");
            _ = context.CloseAsync();
            return null;
        }
        finally
        {
            if (frame is not null)
            {
                ReferenceCountUtil.Release(frame);
            }
        }
    }
}
