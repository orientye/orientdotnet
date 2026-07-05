using DotNetty.Transport.Channels;

namespace Orient.Rpc.Server;

/// <summary>
/// Logs when a child channel crosses DotNetty write-buffer water marks (becomes not writable).
/// Does not stop reads, drop messages, or close the connection — visibility only.
/// </summary>
internal sealed class CRpcServerWriteBufferWarningHandler : ChannelHandlerAdapter
{
    public override void ChannelWritabilityChanged(IChannelHandlerContext context)
    {
        if (!context.Channel.IsWritable)
        {
            Console.WriteLine(
                $"CRpcServer write buffer warning: remote={context.Channel.RemoteAddress}, writable=false");
        }

        context.FireChannelWritabilityChanged();
    }
}
