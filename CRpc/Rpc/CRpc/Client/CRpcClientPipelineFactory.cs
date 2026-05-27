using CRpc.Rpc.CRpc.Codec;
using CRpc.Transport;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Client;

internal sealed class CRpcClientPipelineFactory : IChannelPipelineFactory
{
    private readonly CRpcClientOptions options;

    public CRpcClientPipelineFactory(CRpcClientOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Configure(IChannelPipeline pipeline, TcpChannelHost host)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(host);

        pipeline.AddLast(
            "timeout",
            new IdleStateHandler(0, 0, options.HeartbeatIdleSeconds));
        pipeline.AddLast(
            "decoder",
            new CRpcMessageDecoder(options.MaxFrameLength, options.HashLength));
        pipeline.AddLast(
            "encoder",
            new CRpcMessageEncoder(options.HashLength, options.CompressThreshold));
        pipeline.AddLast("handler", new LoopInboundHandler(host));
    }
}
