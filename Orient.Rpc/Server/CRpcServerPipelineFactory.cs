using Orient.Rpc.Codec;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels;
using Orient.Logging;

namespace Orient.Rpc.Server;

public sealed class CRpcServerPipelineFactory
{
    private readonly CRpcServerOptions options;
    private readonly IOrientLogger decoderLogger;
    private readonly IOrientLogger writeBufferWarningLogger;

    public CRpcServerPipelineFactory(CRpcServerOptions options)
        : this(
            options,
            options?.LoggerFactory ?? NullOrientLoggerFactory.Instance)
    {
    }

    internal CRpcServerPipelineFactory(
        CRpcServerOptions options,
        IOrientLoggerFactory loggerFactory)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(loggerFactory);
        decoderLogger = loggerFactory.CreateLogger("Orient.Rpc.Codec.CRpcMessageDecoder");
        writeBufferWarningLogger = loggerFactory.CreateLogger(
            "Orient.Rpc.Server.CRpcServerWriteBufferWarningHandler");
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
            pipeline.AddLast(
                "write-buffer-warning",
                new CRpcServerWriteBufferWarningHandler(writeBufferWarningLogger));
        }

        pipeline.AddLast("decoder", new CRpcMessageDecoder(options.MaxFrameLength, decoderLogger));
        pipeline.AddLast("encoder", new CRpcMessageEncoder());
        pipeline.AddLast("handler", appHandler);
    }
}
