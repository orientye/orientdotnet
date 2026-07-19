using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;
using Orient.Logging;
using Orient.Rpc.Logging;

namespace Orient.Rpc.Codec;

public sealed class CRpcMessageDecoder : LengthFieldBasedFrameDecoder
{
    private readonly IOrientLogger logger;

    public CRpcMessageDecoder(int maxFrameLength, IOrientLogger logger)
        : base(maxFrameLength, lengthFieldOffset: 4, lengthFieldLength: 4, lengthAdjustment: 0, initialBytesToStrip: 8)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override object Decode(IChannelHandlerContext context, IByteBuffer input)
    {
        if (input.ReadableBytes < CRpcMessage.FramePrefixLength)
        {
            return null;
        }

        var magic = input.GetInt(input.ReaderIndex);
        if (magic != CRpcMessage.Magic)
        {
            if (logger.IsEnabled(OrientLogLevel.Error))
            {
                logger.Error(
                    OrientRpcLogEventIds.DecodeFailed,
                    $"{context.Channel} CRpc decode failed, closing connection: invalid magic 0x{magic:X8}.");
            }
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
            if (logger.IsEnabled(OrientLogLevel.Error))
            {
                logger.Error(
                    OrientRpcLogEventIds.DecodeFailed,
                    $"{context.Channel} CRpc decode failed, closing connection.",
                    exception);
            }
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
