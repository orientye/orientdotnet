using CRpc.Rpc.CRpc.Client;
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Buffers;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests;

public sealed class CRpcClientHeartbeatHandlerTests
{
    [Fact]
    public void WriterIdleEventWritesHeartbeatFrame()
    {
        var channel = new EmbeddedChannel(
            new CRpcMessageEncoder(),
            new CRpcClientHeartbeatHandler());
        channel.Pipeline.FireUserEventTriggered(CreateWriterIdleEvent());

        var outbound = channel.ReadOutbound<IByteBuffer>();
        Assert.NotNull(outbound);
        try
        {
            Assert.Equal(CRpcMessage.Magic, outbound.ReadInt());
            var payloadLen = outbound.ReadInt();
            Assert.Equal(CRpcMessageHeader.FixedLength, payloadLen);
            var header = CRpcMessageHeader.ReadFrom(outbound);
            Assert.Equal(CRpcMessageType.Heartbeat, header.MessageType);
        }
        finally
        {
            outbound.Release();
        }
    }

    private static IdleStateEvent CreateWriterIdleEvent()
    {
        return CreateIdleStateEvent(IdleState.WriterIdle);
    }

    internal static IdleStateEvent CreateIdleStateEvent(IdleState state)
    {
        var ctor = typeof(IdleStateEvent).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(IdleState), typeof(bool) },
            modifiers: null);
        Assert.NotNull(ctor);
        return (IdleStateEvent)ctor!.Invoke(new object[] { state, false });
    }
}
