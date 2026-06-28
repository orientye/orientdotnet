using Orient.Rpc.Transport;
using DotNetty.Transport.Channels;

namespace CRPC.Tests.Transport;

public sealed class IChannelPipelineFactoryTests
{
    [Fact]
    public void FactoryCanBeImplementedByProtocolSpecificCode()
    {
        IChannelPipelineFactory factory = new RecordingPipelineFactory();

        Assert.NotNull(factory);
    }

    private sealed class RecordingPipelineFactory : IChannelPipelineFactory
    {
        public void Configure(IChannelPipeline pipeline, TcpChannelHost host)
        {
            ArgumentNullException.ThrowIfNull(pipeline);
            ArgumentNullException.ThrowIfNull(host);
        }
    }
}
