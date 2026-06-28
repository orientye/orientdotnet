using Orient.Rpc.Codec;
using DotNetty.Buffers;
using DotNetty.Transport.Channels.Embedded;

namespace Orient.Tests;

public class CRpcMessageEncoderTests
{
    [Fact]
    public void EncodeWritesCrpcMagicAndPayloadLength()
    {
        var encoder = new CRpcMessageEncoder();
        var channel = new EmbeddedChannel(encoder);
        var message = CRpcMessage.Create(
            CRpcMessageType.Request,
            serviceId: 7,
            methodId: 3,
            reqSequence: 42,
            resultCode: 0,
            body: new byte[] { 1, 2, 3 });

        Assert.True(channel.WriteOutbound(message));

        var frame = channel.ReadOutbound<IByteBuffer>();
        Assert.NotNull(frame);
        try
        {
            Assert.Equal(CRpcMessage.Magic, frame.GetInt(frame.ReaderIndex));
            Assert.Equal(
                CRpcMessageHeader.FixedLength + 3,
                frame.GetInt(frame.ReaderIndex + sizeof(int)));
        }
        finally
        {
            frame.Release();
        }
    }

    [Fact]
    public void EncodeProducesExactFrameLength()
    {
        var encoder = new CRpcMessageEncoder();
        var channel = new EmbeddedChannel(encoder);
        var message = CRpcMessage.Create(
            CRpcMessageType.Request,
            serviceId: 1,
            methodId: 1,
            reqSequence: 1,
            resultCode: 0,
            body: new byte[] { 9, 8, 7 });

        Assert.True(channel.WriteOutbound(message));

        var frame = channel.ReadOutbound<IByteBuffer>();
        Assert.NotNull(frame);
        try
        {
            Assert.Equal(message.GetFrameLength(), frame.ReadableBytes);
        }
        finally
        {
            frame.Release();
        }
    }
}
