using CRpc.Rpc.CRpc.Codec;
using CRpc.Rpc.CRpc.Server;
using DotNetty.Buffers;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests;

public class CRpcMessageEncoderTests
{
    [Fact]
    public void EncodeWritesCrpcFrameWithMagicNumber()
    {
        var encoder = new CRpcMessageEncoder(
            CRpcServerOptions.DefaultHashLength,
            CRpcServerOptions.DefaultCompressThreshold);
        var channel = new EmbeddedChannel(encoder);
        var header = CRpcMessageHeader.valueOf(
            CRpcMessageState.STATE_NONE,
            resultCode: 0,
            sn: 42,
            module: 7,
            command: 3);
        header.addState(CRpcMessageState.NONE_ENCRYPT);
        var message = CRpcMessage.valueOf(header, new byte[] { 1, 2, 3 });

        Assert.True(channel.WriteOutbound(message));

        var frame = channel.ReadOutbound<IByteBuffer>();
        Assert.NotNull(frame);
        try
        {
            Assert.Equal(CRpcMessage.MAGIC_NUM, frame.GetInt(frame.ReaderIndex));
            Assert.Equal(message.getSize(), frame.GetInt(frame.ReaderIndex + CRpcMessage.MAGIC));
        }
        finally
        {
            frame.Release();
        }
    }

    [Fact]
    public void EncodePreSizesBufferToFrameLength()
    {
        var encoder = new CRpcMessageEncoder(
            CRpcServerOptions.DefaultHashLength,
            CRpcServerOptions.DefaultCompressThreshold);
        var channel = new EmbeddedChannel(encoder);
        var header = CRpcMessageHeader.valueOf(
            CRpcMessageState.STATE_NONE,
            resultCode: 0,
            sn: 1,
            module: 1,
            command: 1);
        header.addState(CRpcMessageState.NONE_ENCRYPT);
        var message = CRpcMessage.valueOf(header, new byte[] { 9, 8, 7 });

        Assert.True(channel.WriteOutbound(message));

        var frame = channel.ReadOutbound<IByteBuffer>();
        Assert.NotNull(frame);
        try
        {
            Assert.Equal(message.getSize(), frame.ReadableBytes);
        }
        finally
        {
            frame.Release();
        }
    }
}
