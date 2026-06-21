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

        options.Validate();

        if (options.HeartbeatEnabled)
        {
            pipeline.AddLast(
                "idle",
                new IdleStateHandler(0, options.HeartbeatIntervalSeconds, 0));
            pipeline.AddLast("heartbeat", new CRpcClientHeartbeatHandler());
        }

        pipeline.AddLast(
            "decoder",
            new CRpcMessageDecoder(options.MaxFrameLength));
        pipeline.AddLast(
            "encoder",
            new CRpcMessageEncoder());
        pipeline.AddLast("handler", new LoopInboundHandler(host));
    }
}
