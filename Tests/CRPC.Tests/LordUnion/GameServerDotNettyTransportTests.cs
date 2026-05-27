using CRpc.Async;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Sessions;

namespace CRPC.Tests.LordUnion;

public sealed class GameServerDotNettyTransportTests
{
    [Fact]
    public void DeliverFrameDecodesProtocolMessageOnSessionLoop()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var codec = new ServerProtocolCodec();
        var session = new AccountSession(loop, "player1", codec);
        var transport = new GameServerDotNettyTransport(codec);
        transport.BindIncomingHandler(session, codec);
        var packet = codec.EncodeClientRequest(new TKMobileReqMsg { Param = 7 });
        var requestMessage = codec.DecodePacket(packet);
        var frame = new GameServerFrame(requestMessage.Header0, packet.Skip(ServerPacketFrame.HeaderLength).ToArray());

        transport.DeliverFrameForTesting(frame);
        loop.Tick();

        var received = Assert.Single(session.ReceivedMessages);
        Assert.Equal(SessionMessageDirection.Received, received.Direction);
        Assert.Equal(requestMessage.Header0, received.Header0);
    }

    [Fact]
    public void BuildOutboundFrameForTestingPreservesExistingPacketBytes()
    {
        var codec = new ServerProtocolCodec();
        var packet = codec.EncodeClientRequest(new TKMobileReqMsg { Param = 11 });
        var transport = new GameServerDotNettyTransport(codec);

        var frame = transport.BuildOutboundFrameForTesting(packet);
        var encoded = ServerPacketFrame.EncodeFrame(frame.Header0, frame.Body);

        Assert.Equal(packet, encoded);
    }
}
