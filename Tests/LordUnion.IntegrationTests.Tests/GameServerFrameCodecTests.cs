using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels.Embedded;
using LordUnion.IntegrationTests.Protocol;

namespace LordUnion.IntegrationTests.Tests;

public sealed class GameServerFrameCodecTests
{
    [Fact]
    public void EncoderMatchesExistingServerPacketFrameBytes()
    {
        var body = new byte[] { 1, 2, 3, 4 };
        var frame = new GameServerFrame(ServerPacketFrame.ClientSendHeaderMagic, body);
        var channel = new EmbeddedChannel(new GameServerFrameEncoder());

        Assert.True(channel.WriteOutbound(frame));
        var buffer = channel.ReadOutbound<IByteBuffer>();
        var bytes = new byte[buffer.ReadableBytes];
        buffer.ReadBytes(bytes);

        Assert.Equal(ServerPacketFrame.EncodeFrame(ServerPacketFrame.ClientSendHeaderMagic, body), bytes);
    }

    [Fact]
    public void DecoderReadsCompleteFrame()
    {
        var body = new byte[] { 9, 8, 7 };
        var packet = ServerPacketFrame.EncodeFrame(1001, body);
        var input = Unpooled.WrappedBuffer(packet);
        var channel = new EmbeddedChannel(new GameServerFrameDecoder(maxBodyLength: 1024));

        Assert.True(channel.WriteInbound(input));
        var frame = channel.ReadInbound<GameServerFrame>();

        Assert.Equal(1001u, frame.Header0);
        Assert.Equal(body, frame.Body);
    }

    [Fact]
    public void DecoderWaitsForPartialBody()
    {
        var body = new byte[] { 5, 6, 7, 8 };
        var packet = ServerPacketFrame.EncodeFrame(1001, body);
        var firstHalf = Unpooled.WrappedBuffer(packet.AsSpan(0, 10).ToArray());
        var secondHalf = Unpooled.WrappedBuffer(packet.AsSpan(10).ToArray());
        var channel = new EmbeddedChannel(new GameServerFrameDecoder(maxBodyLength: 1024));

        Assert.False(channel.WriteInbound(firstHalf));
        Assert.True(channel.WriteInbound(secondHalf));
        var frame = channel.ReadInbound<GameServerFrame>();

        Assert.Equal(1001u, frame.Header0);
        Assert.Equal(body, frame.Body);
    }

    [Fact]
    public void DecoderRejectsNegativeBodyLength()
    {
        var bytes = new byte[ServerPacketFrame.HeaderLength];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), 1001u);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), -1);
        var channel = new EmbeddedChannel(new GameServerFrameDecoder(maxBodyLength: 1024));

        var exception = Assert.Throws<DecoderException>(() =>
            channel.WriteInbound(Unpooled.WrappedBuffer(bytes)));
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }
}
