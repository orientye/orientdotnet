using DotNetty.Transport.Channels;

namespace CRpc.Transport;

public interface IChannelPipelineFactory
{
    void Configure(IChannelPipeline pipeline, TcpChannelHost host);
}
