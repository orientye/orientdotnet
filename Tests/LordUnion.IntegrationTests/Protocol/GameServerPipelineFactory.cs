using CRpc.Transport;
using DotNetty.Transport.Channels;

namespace LordUnion.IntegrationTests.Protocol;

public sealed class GameServerPipelineFactory : IChannelPipelineFactory
{
    private readonly int maxBodyLength;

    public GameServerPipelineFactory(int maxBodyLength = 1024 * 1024)
    {
        this.maxBodyLength = maxBodyLength;
    }

    public void Configure(IChannelPipeline pipeline, TcpChannelHost host)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(host);

        pipeline.AddLast("game-server-decoder", new GameServerFrameDecoder(maxBodyLength));
        pipeline.AddLast("game-server-encoder", new GameServerFrameEncoder());
        pipeline.AddLast("loop-ingress", new LoopInboundHandler(host));
    }
}