using DotNetty.Buffers;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc;

/// <summary>
/// DotNetty outbound write helpers. Does not await write completion or touch business loop state.
/// Release ownership:
/// <list type="bullet">
/// <item><see cref="WriteAndFlushFireAndForget"/> — caller-owned message; released on synchronous submit failure.</item>
/// <item><see cref="WriteEncodedFrame"/> / <see cref="WriteEncodedFrameFireAndForget"/> — util-owned frame; released in
/// <c>WriteEncodedFrame</c> when encode or submit fails before pipeline ownership.</item>
/// </list>
/// </summary>
internal static class ChannelWriteUtil
{
    public static void WriteAndFlushFireAndForget(IChannelHandlerContext ctx, object message)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        WriteAndFlushFireAndForget(ctx.Channel, message);
    }

    /// <summary>
    /// Submits a caller-owned message without observing write completion. Releases the message when
    /// <see cref="IChannel.WriteAndFlushAsync"/> throws synchronously.
    /// </summary>
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
        WriteEncodedFrameFireAndForget(ctx.Channel, size, encode);
    }

    /// <summary>
    /// Encodes and submits a frame without observing write completion. Frame release on synchronous
    /// encode/submit failure is handled by the shared <c>WriteEncodedFrame</c> helper.
    /// </summary>
    public static void WriteEncodedFrameFireAndForget(
        IChannel channel,
        int size,
        Action<IByteBuffer> encode)
    {
        _ = WriteEncodedFrame(channel, size, encode);
    }

    /// <summary>
    /// Encodes and submits a frame, returning the DotNetty write task. The encoded frame is released
    /// on synchronous encode/submit failure; otherwise ownership transfers to the channel pipeline.
    /// </summary>
    public static Task WriteEncodedFrame(
        IChannel channel,
        int size,
        Action<IByteBuffer> encode)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(encode);
        return WriteEncodedFrame(channel.Allocator, frame => WriteAndFlush(channel, frame), size, encode);
    }

    private static Task WriteAndFlush(IChannel channel, object message)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(message);
        return channel.WriteAndFlushAsync(message);
    }

    private static Task WriteEncodedFrame(
        IByteBufferAllocator allocator,
        Func<IByteBuffer, Task> submit,
        int size,
        Action<IByteBuffer> encode)
    {
        IByteBuffer? frame = allocator.DirectBuffer(size);
        try
        {
            encode(frame);
            var writeTask = submit(frame);
            frame = null;
            return writeTask;
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
