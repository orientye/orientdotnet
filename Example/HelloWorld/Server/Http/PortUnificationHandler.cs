using Orient.Runtime;
using Orient.Rpc.Codec;
using Orient.Rpc.Server;
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;

namespace Example.Http;

public sealed class PortUnificationHandler : ByteToMessageDecoder
{
    private readonly OrientExecutor loop;
    private readonly CRpcConnectionRegistry connections;
    private readonly Action<IChannelHandlerContext> configureCrpc;
    private readonly Action<IChannelHandlerContext> configureHttp;

    public PortUnificationHandler(
        OrientExecutor loop,
        CRpcConnectionRegistry connections,
        Action<IChannelHandlerContext> configureCrpc,
        Action<IChannelHandlerContext> configureHttp)
    {
        this.loop = loop;
        this.connections = connections;
        this.configureCrpc = configureCrpc;
        this.configureHttp = configureHttp;
    }

    protected override void Decode(IChannelHandlerContext context, IByteBuffer input, List<object> output)
    {
        if (input.ReadableBytes < 4)
        {
            return;
        }

        input.MarkReaderIndex();
        var magic = input.ReadInt();
        input.ResetReaderIndex();

        if (magic == CRpcMessage.Magic)
        {
            configureCrpc(context);
        }
        else
        {
            configureHttp(context);
        }

        var channel = context.Channel;
        loop.Post(() => connections.Register(channel));
        context.Channel.Pipeline.Remove(this);
    }
}
