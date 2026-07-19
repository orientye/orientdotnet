using Orient.Rpc.Codec;
using Orient.Rpc.Transport;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels;
using Orient.Logging;

namespace Orient.Rpc.Client;

internal sealed class CRpcClientPipelineFactory : IChannelPipelineFactory
{
    private readonly CRpcClientOptions options;
    private readonly IOrientLogger decoderLogger;

    public CRpcClientPipelineFactory(CRpcClientOptions options, IOrientLogger decoderLogger)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.decoderLogger = decoderLogger ?? throw new ArgumentNullException(nameof(decoderLogger));
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
            new CRpcMessageDecoder(options.MaxFrameLength, decoderLogger));
        pipeline.AddLast(
            "encoder",
            new CRpcMessageEncoder());
        pipeline.AddLast("handler", new ExecutorInboundHandler(host));
    }
}
