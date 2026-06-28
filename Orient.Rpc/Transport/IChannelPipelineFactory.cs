using DotNetty.Transport.Channels;

namespace Orient.Rpc.Transport;

public interface IChannelPipelineFactory
{
    void Configure(IChannelPipeline pipeline, TcpChannelHost host);
}
