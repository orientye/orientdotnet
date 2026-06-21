using CRpc.Rpc.CRpc.Codec;
using DotNetty.Buffers;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests;

public class CRpcCodecTests
{
    [Fact]
    public void HeaderWriteProducesTwentyFourBytesWithExpectedOffsets()
    {
        var header = CRpcMessageHeader.Create(
            CRpcMessageType.Request,
            serviceId: 7,
            methodId: 3,
            reqSequence: 42,
            resultCode: 0,
            body: new byte[] { 1, 2, 3 });

        var buffer = Unpooled.Buffer();
        header.WriteTo(buffer);

        try
        {
            Assert.Equal(CRpcMessageHeader.FixedLength, buffer.ReadableBytes);
            Assert.Equal(1, buffer.ReadByte());                          // version
            Assert.Equal((byte)CRpcMessageType.Request, buffer.ReadByte());
            Assert.Equal(CRpcFrameFlags.None, buffer.ReadByte());        // flags
            Assert.Equal(0, buffer.ReadByte());                          // reserved
            Assert.Equal(7, buffer.ReadUnsignedShort());                 // serviceId
            Assert.Equal(3, buffer.ReadUnsignedShort());                 // methodId
            Assert.Equal(42L, buffer.ReadLong());                        // reqSeq
            Assert.Equal(0, buffer.ReadInt());                           // resultCode
            Assert.Equal(3, buffer.ReadInt());                             // bodyOriginLen
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void HeaderRoundTripPreservesFields()
    {
        var original = CRpcMessageHeader.Create(
            CRpcMessageType.Push,
            serviceId: 1000,
            methodId: 2,
            reqSequence: 0,
            resultCode: 0,
            body: new byte[] { 9 });

        var buffer = Unpooled.Buffer();
        original.WriteTo(buffer);

        try
        {
            var restored = CRpcMessageHeader.ReadFrom(buffer);
            Assert.Equal(original.MessageType, restored.MessageType);
            Assert.Equal(original.ServiceId, restored.ServiceId);
            Assert.Equal(original.MethodId, restored.MethodId);
            Assert.Equal(original.ReqSequence, restored.ReqSequence);
            Assert.Equal(original.ResultCode, restored.ResultCode);
            Assert.Equal(original.BodyOriginLen, restored.BodyOriginLen);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void ToFrameWritesCrpcMagicAndPayloadLength()
    {
        var message = CRpcMessage.Create(
            CRpcMessageType.Request,
            serviceId: 7,
            methodId: 3,
            reqSequence: 42,
            resultCode: 0,
            body: new byte[] { 1, 2, 3 });

        var buffer = Unpooled.Buffer();
        message.WriteTo(buffer);

        try
        {
            Assert.Equal(CRpcMessage.MinFrameLength + 3, buffer.ReadableBytes);
            Assert.Equal(CRpcMessage.Magic, buffer.ReadInt());
            Assert.Equal(CRpcMessageHeader.FixedLength + 3, buffer.ReadInt());
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void ReadFromFrameRoundTripsRequest()
    {
        var original = CRpcMessage.Create(
            CRpcMessageType.Request,
            serviceId: 1,
            methodId: 2,
            reqSequence: 99,
            resultCode: 0,
            body: new byte[] { 5, 6 });

        var buffer = Unpooled.Buffer();
        original.WriteTo(buffer);

        try
        {
            var restored = CRpcMessage.ReadFrom(buffer);
            Assert.Equal(original.Header.MessageType, restored.Header.MessageType);
            Assert.Equal(original.ServiceId, restored.ServiceId);
            Assert.Equal(original.MethodId, restored.MethodId);
            Assert.Equal(original.ReqSequence, restored.ReqSequence);
            Assert.Equal(original.Body, restored.Body);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void CreateResponseSetsMessageTypeResponse()
    {
        var request = CRpcMessage.Create(
            CRpcMessageType.Request,
            serviceId: 10,
            methodId: 20,
            reqSequence: 5,
            resultCode: 0,
            body: Array.Empty<byte>());

        var response = request.CreateResponse(resultCode: 0, body: new byte[] { 7 });

        Assert.Equal(CRpcMessageType.Response, response.Header.MessageType);
        Assert.Equal(5L, response.ReqSequence);
        Assert.Equal(10, response.ServiceId);
        Assert.Equal(20, response.MethodId);
        Assert.Equal(new byte[] { 7 }, response.Body);
    }

    [Fact]
    public void DecoderRoundTripsEncodedRequestThroughPipeline()
    {
        var encoder = new CRpcMessageEncoder();
        var decoder = new CRpcMessageDecoder(maxFrameLength: 1024 * 1024);
        var original = CRpcMessage.Create(
            CRpcMessageType.Request,
            serviceId: 1000,
            methodId: 1,
            reqSequence: 42,
            resultCode: 0,
            body: new byte[] { 1, 2, 3 });

        var encodeChannel = new EmbeddedChannel(encoder);
        Assert.True(encodeChannel.WriteOutbound(original));

        var frame = encodeChannel.ReadOutbound<IByteBuffer>();
        Assert.NotNull(frame);

        var decodeChannel = new EmbeddedChannel(decoder);
        Assert.True(decodeChannel.WriteInbound(frame));

        var decoded = decodeChannel.ReadInbound<CRpcMessage>();
        Assert.NotNull(decoded);
        Assert.Equal(original.ServiceId, decoded.ServiceId);
        Assert.Equal(original.MethodId, decoded.MethodId);
        Assert.Equal(original.ReqSequence, decoded.ReqSequence);
        Assert.Equal(original.Body, decoded.Body);
        Assert.Equal(CRpcMessageType.Request, decoded.MessageType);
    }

    [Fact]
    public void CreateHeartbeatProducesExpectedHeaderFields()
    {
        var message = CRpcMessage.CreateHeartbeat();

        Assert.Equal(CRpcMessageType.Heartbeat, message.MessageType);
        Assert.Equal(0, message.ServiceId);
        Assert.Equal(0, message.MethodId);
        Assert.Equal(0, message.ReqSequence);
        Assert.Equal(0, message.ResultCode);
        Assert.Empty(message.Body);
    }

    [Fact]
    public void HeartbeatRoundTripThroughEncoderDecoder()
    {
        var original = CRpcMessage.CreateHeartbeat();
        var encoder = new CRpcMessageEncoder();
        var decoder = new CRpcMessageDecoder(maxFrameLength: 1024);
        var encodeChannel = new EmbeddedChannel(encoder);
        Assert.True(encodeChannel.WriteOutbound(original));
        var frame = encodeChannel.ReadOutbound<IByteBuffer>();
        Assert.NotNull(frame);

        var decodeChannel = new EmbeddedChannel(decoder);
        Assert.True(decodeChannel.WriteInbound(frame.Retain()));
        var decoded = decodeChannel.ReadInbound<CRpcMessage>();

        Assert.Equal(CRpcMessageType.Heartbeat, decoded.MessageType);
        Assert.Equal(0, decoded.ServiceId);
        Assert.Equal(0, decoded.MethodId);
        Assert.Equal(0, decoded.ReqSequence);
    }

    [Fact]
    public void HeaderReadFromRejectsUnknownMessageTypeAboveHeartbeat()
    {
        var header = CRpcMessageHeader.Create(
            CRpcMessageType.Request, 0, 0, 0, 0, Array.Empty<byte>());
        var buffer = Unpooled.Buffer();
        header.WriteTo(buffer);
        buffer.SetByte(1, 5);

        Assert.Throws<InvalidDataException>(() => CRpcMessageHeader.ReadFrom(buffer));
    }
}
