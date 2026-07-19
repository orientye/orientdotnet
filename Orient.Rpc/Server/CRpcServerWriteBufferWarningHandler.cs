using DotNetty.Transport.Channels;
using Orient.Logging;
using Orient.Rpc.Logging;

namespace Orient.Rpc.Server;

/// <summary>
/// Logs when a child channel crosses DotNetty write-buffer water marks (becomes not writable).
/// Does not stop reads, drop messages, or close the connection — visibility only.
/// </summary>
internal sealed class CRpcServerWriteBufferWarningHandler : ChannelHandlerAdapter
{
    private readonly IOrientLogger logger;

    public CRpcServerWriteBufferWarningHandler(IOrientLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override void ChannelWritabilityChanged(IChannelHandlerContext context)
    {
        if (!context.Channel.IsWritable && logger.IsEnabled(OrientLogLevel.Warn))
        {
            logger.Warn(
                OrientRpcLogEventIds.WriteBufferWarning,
                $"CRpcServer write buffer warning: remote={context.Channel.RemoteAddress}, writable=false");
        }

        context.FireChannelWritabilityChanged();
    }
}
