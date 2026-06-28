using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;

namespace Orient.Rpc.Util;

/// <summary>
/// DotNetty outbound write helpers. Does not await write completion or touch business loop state.
/// Releases the message when <see cref="IChannel.WriteAndFlushAsync"/> throws synchronously.
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
}
