using Orient.Rpc.Codec;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels;

namespace Orient.Rpc.Server;

public sealed class CRpcServerPipelineFactory
{
    private readonly CRpcServerOptions options;

    public CRpcServerPipelineFactory(CRpcServerOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Configure(IChannelPipeline pipeline, IChannelHandler appHandler)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(appHandler);

        options.Validate();

        if (options.HeartbeatEnabled)
        {
            pipeline.AddLast(
                "idle",
                new IdleStateHandler(options.ReadIdleSeconds, 0, 0));
            pipeline.AddLast("idle-handler", new CRpcServerReadIdleHandler());
        }

        if (options.WriteBufferWarningEnabled)
        {
            pipeline.AddLast("write-buffer-warning", new CRpcServerWriteBufferWarningHandler());
        }

        pipeline.AddLast("decoder", new CRpcMessageDecoder(options.MaxFrameLength));
        pipeline.AddLast("encoder", new CRpcMessageEncoder());
        pipeline.AddLast("handler", appHandler);
    }
}
