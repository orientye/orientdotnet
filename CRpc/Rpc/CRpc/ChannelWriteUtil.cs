using DotNetty.Buffers;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc;

/// <summary>
/// DotNetty outbound write helpers. Does not await write completion or touch business loop state.
/// Releases the message only when write submission fails synchronously (before pipeline ownership).
/// </summary>
internal static class ChannelWriteUtil
{
    public static void WriteAndFlushFireAndForget(IChannelHandlerContext ctx, object message)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        WriteAndFlushFireAndForget(ctx.Channel, message);
    }

    public static void WriteAndFlushFireAndForget(IChannel channel, object message)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            _ = channel.WriteAndFlushAsync(message);
        }
        catch
        {
            ReferenceCountUtil.Release(message);
            throw;
        }
    }

    public static void WriteEncodedFrameFireAndForget(
        IChannelHandlerContext ctx,
        int size,
        Action<IByteBuffer> encode)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(encode);
        WriteEncodedFrameFireAndForget(ctx.Allocator, frame => WriteAndFlushFireAndForget(ctx, frame), size, encode);
    }

    public static void WriteEncodedFrameFireAndForget(
        IChannel channel,
        int size,
        Action<IByteBuffer> encode)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(encode);
        WriteEncodedFrameFireAndForget(channel.Allocator, frame => WriteAndFlushFireAndForget(channel, frame), size, encode);
    }

    private static void WriteEncodedFrameFireAndForget(
        IByteBufferAllocator allocator,
        Action<IByteBuffer> submit,
        int size,
        Action<IByteBuffer> encode)
    {
        IByteBuffer? frame = allocator.DirectBuffer(size);
        try
        {
            encode(frame);
            submit(frame);
            frame = null;
        }
        finally
        {
            if (frame is not null)
            {
                ReferenceCountUtil.SafeRelease(frame);
            }
        }
    }
}
