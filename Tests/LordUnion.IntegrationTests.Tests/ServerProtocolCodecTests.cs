using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Sessions;
using ProtoBuf;

namespace LordUnion.IntegrationTests.Tests;

public class ServerProtocolCodecTests
{
    private readonly ServerProtocolCodec _codec = new();

    [Fact]
    public void TryReadFrameHeader_ValidPacket_ReturnsExpectedFields()
    {
        var body = new byte[] { 1, 2, 3, 4, 5 };
        var packet = ServerPacketFrame.EncodeFrame(1001, body);

        var ok = _codec.TryReadFrameHeader(
            packet.AsSpan(0, ServerPacketFrame.HeaderLength),
            new ProtocolDecodeContext { Phase = ProtocolPhase.Connect },
            out var frame,
            out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(1001u, frame.Header0);
        Assert.Equal(body.Length, frame.BodyLength);
    }

    [Fact]
    public void TryReadFrameHeader_IncompleteHeader_ReportsStructuredError()
    {
        var ok = _codec.TryReadFrameHeader(
            new byte[] { 1, 2, 3 },
            new ProtocolDecodeContext
            {
                AccountAlias = "player1",
                Phase = ProtocolPhase.Login,
            },
            out _,
            out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Equal("player1", error!.AccountAlias);
        Assert.Equal(ProtocolPhase.Login, error.Phase);
        Assert.Null(error.Header0);
        Assert.Equal(3, error.BodyLength);
        Assert.Contains("Header must be at least", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryReadFrameHeader_InvalidBodyLength_ReportsStructuredError()
    {
        var header = new byte[ServerPacketFrame.HeaderLength];
        BitConverter.TryWriteBytes(header.AsSpan(0, 4), 42u);
        BitConverter.TryWriteBytes(header.AsSpan(4, 4), -5);

        var ok = _codec.TryReadFrameHeader(
            header,
            new ProtocolDecodeContext { Phase = ProtocolPhase.AnonymousBrowse },
            out _,
            out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Equal(42u, error!.Header0);
        Assert.Equal(-5, error.BodyLength);
        Assert.Equal(42u, error.MessageId);
        Assert.Contains("Invalid body length", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryReadFrameHeader_UnknownRoutingField_ReportsStructuredError()
    {
        var body = new byte[] { 9, 8, 7 };
        var packet = ServerPacketFrame.EncodeFrame(9999, body);

        var ok = _codec.TryReadFrameHeader(
            packet.AsSpan(0, ServerPacketFrame.HeaderLength),
            new ProtocolDecodeContext
            {
                Phase = ProtocolPhase.Login,
                ExpectedHeader0 = 1001,
            },
            out _,
            out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Equal(9999u, error!.Header0);
        Assert.Equal(9999u, error.MessageId);
        Assert.Contains("Unexpected routing header0", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryDecodePacket_TruncatedBody_ReportsStructuredError()
    {
        var body = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var header = new byte[ServerPacketFrame.HeaderLength];
        BitConverter.TryWriteBytes(header.AsSpan(0, 4), 2001u);
        BitConverter.TryWriteBytes(header.AsSpan(4, 4), body.Length);
        var packet = header.Concat(body.Take(3)).ToArray();

        var ok = _codec.TryDecodePacket(
            packet,
            new ProtocolDecodeContext
            {
                AccountAlias = "player2",
                Phase = ProtocolPhase.Login,
            },
            out _,
            out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Equal("player2", error!.AccountAlias);
        Assert.Equal(2001u, error.Header0);
        Assert.Equal(body.Length, error.BodyLength);
        Assert.Contains("Packet truncated", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EncodeClientRequest_AnonymousBrowseReq_RoundTripsThroughDecode()
    {
        var request = _codec.CreateAnonymousBrowseRequest(77);
        var packet = _codec.EncodeClientRequest(request);
        var decoded = _codec.DecodePacket(packet);

        Assert.Equal(ProtocolMessageKind.AnonymousBrowseReq, decoded.Kind);
        Assert.Equal(77u, decoded.AnonymousBrowseRequest?.Serialid);
    }

    [Fact]
    public void EncodeClientRequest_CommonLoginReq_RoundTripsThroughDecode()
    {
        var request = _codec.CreatePasswordLoginRequest(
            "TJJ006628",
            "3YXRQW",
            LobbyAes128Crypto.DefaultKey,
            appId: 2,
            timestampMillis: 1_700_000_000_000);
        var packet = _codec.EncodeClientRequest(request);
        var decoded = _codec.DecodePacket(packet);

        Assert.Equal(ProtocolMessageKind.CommonLoginReq, decoded.Kind);
        Assert.NotNull(decoded.CommonLoginRequest);
        Assert.Equal(1u, decoded.CommonLoginRequest!.Cryptotype);
        Assert.Equal(LoginWireConstants.GeneralPasswordLoginMsgType, decoded.CommonLoginRequest.Msgtype);
    }

    [Fact]
    public void DecodeBody_CommonLoginAck_IdentifiesMessageKind()
    {
        var ack = new TKMobileAckMsg
        {
            Param = 0,
            LobbyAckMsg = new LobbyAckMsg
            {
                CommonloginAckMsg = new CommonLoginAck
                {
                    Userinfo = new LcUserInfoEx
                    {
                        Userid = 12345,
                        Nickname = "tester",
                    },
                },
            },
        };

        using var bodyStream = new MemoryStream();
        Serializer.Serialize(bodyStream, ack);
        var body = bodyStream.ToArray();
        var packet = ServerPacketFrame.EncodeFrame(3001, body);

        var decoded = _codec.DecodePacket(
            packet,
            new ProtocolDecodeContext { Phase = ProtocolPhase.Login });

        Assert.Equal(ProtocolMessageKind.CommonLoginAck, decoded.Kind);
        Assert.Equal(12345u, decoded.CommonLoginAcknowledgement?.Userinfo?.Userid);
    }

    [Fact]
    public void EncodeClientRequest_TourneySignupReq_RoundTripsThroughDecode()
    {
        var request = _codec.CreateTourneySignupRequest(
            userId: 100,
            tourneyId: 159740,
            gameId: 1001,
            matchPoint: 2008280,
            nickname: "player1");

        var packet = _codec.EncodeClientRequest(request);
        var decoded = _codec.DecodePacket(packet);

        Assert.Equal(ProtocolMessageKind.TourneySignupReq, decoded.Kind);
        Assert.Equal(159740u, decoded.TourneySignupRequest?.Tourneyid);
        Assert.Equal(1001u, decoded.TourneySignupRequest?.Gameid);
        Assert.Equal(2008280u, decoded.TourneySignupRequest?.Matchpoint);
    }

    [Fact]
    public void DecodeBody_TourneySignupAck_IdentifiesMessageKind()
    {
        var ack = new TKMobileAckMsg
        {
            LobbyAckMsg = new LobbyAckMsg
            {
                TourneysignupexAckMsg = new TourneySignupExAck
                {
                    Tourneyid = 159740,
                    Matchpoint = 2008280,
                    Gameid = 1001,
                },
            },
        };

        using var bodyStream = new MemoryStream();
        Serializer.Serialize(bodyStream, ack);
        var packet = ServerPacketFrame.EncodeFrame(4001, bodyStream.ToArray());

        var decoded = _codec.DecodePacket(
            packet,
            new ProtocolDecodeContext { Phase = ProtocolPhase.Signup });

        Assert.Equal(ProtocolMessageKind.TourneySignupAck, decoded.Kind);
        Assert.Equal(159740u, decoded.TourneySignupAcknowledgement?.Tourneyid);
    }

    [Fact]
    public void EncodeClientRequest_EnterMatchReq_RoundTripsThroughDecode()
    {
        var ticket = new byte[] { 0x01, 0x02, 0x03 };
        var request = _codec.CreateEnterMatchRequest(matchId: 555, gameId: 1001, ticket: ticket);
        var packet = _codec.EncodeClientRequest(request);
        var decoded = _codec.DecodePacket(packet);

        Assert.Equal(ProtocolMessageKind.EnterMatchReq, decoded.Kind);
        Assert.Equal(1001u, decoded.EnterMatchRequest?.Gameid);
        Assert.Equal(ticket, decoded.EnterMatchRequest?.Ticket);
    }

    [Fact]
    public void DecodeBody_EnterMatchAck_IdentifiesMessageKind()
    {
        var ack = new TKMobileAckMsg
        {
            MatchAckMsg = new MatchAckMsg
            {
                EntermatchAckMsg = new EnterMatchAck
                {
                    Matchid = 555,
                    Matchname = "classic",
                },
            },
        };

        using var bodyStream = new MemoryStream();
        Serializer.Serialize(bodyStream, ack);
        var packet = ServerPacketFrame.EncodeFrame(1001, bodyStream.ToArray());

        var decoded = _codec.DecodePacket(
            packet,
            new ProtocolDecodeContext { Phase = ProtocolPhase.EnterMatch });

        Assert.Equal(ProtocolMessageKind.EnterMatchAck, decoded.Kind);
        Assert.Equal(555u, decoded.EnterMatchAcknowledgement?.Matchid);
    }

    [Fact]
    public void EncodeClientRequest_EnterRoundReq_RoundTripsThroughDecode()
    {
        var request = _codec.CreateEnterRoundRequest(matchId: 777, gameId: 1001, ticket: new byte[] { 0x0A });
        var packet = _codec.EncodeClientRequest(request);
        var decoded = _codec.DecodePacket(packet);

        Assert.Equal(ProtocolMessageKind.EnterRoundReq, decoded.Kind);
        Assert.Equal(1001u, decoded.EnterRoundRequest?.Gameid);
    }

    [Fact]
    public void DecodeBody_EnterRoundAck_IdentifiesMessageKind()
    {
        var ack = new TKMobileAckMsg
        {
            MatchAckMsg = new MatchAckMsg
            {
                EnterroundAckMsg = new EnterRoundAck
                {
                    Seatorder = 2,
                    Usertype = 1,
                },
            },
        };

        using var bodyStream = new MemoryStream();
        Serializer.Serialize(bodyStream, ack);
        var packet = ServerPacketFrame.EncodeFrame(1001, bodyStream.ToArray());

        var decoded = _codec.DecodePacket(
            packet,
            new ProtocolDecodeContext { Phase = ProtocolPhase.EnterRound });

        Assert.Equal(ProtocolMessageKind.EnterRoundAck, decoded.Kind);
        Assert.Equal(2u, decoded.EnterRoundAcknowledgement?.Seatorder);
    }

    [Fact]
    public void EncodeClientRequest_LordReq_RoundTripsThroughDecode()
    {
        var request = _codec.CreateLordClientReadyRequest(matchId: 888, seat: 1);
        var packet = _codec.EncodeClientRequest(request);
        var decoded = _codec.DecodePacket(packet);

        Assert.Equal(ProtocolMessageKind.LordReq, decoded.Kind);
        Assert.Equal(888u, decoded.LordRequest?.Matchid);
        Assert.Equal(1u, decoded.LordRequest?.LordclientreadyReqMsg?.Seat);
    }

    [Fact]
    public void DecodeBody_LordAck_IdentifiesMessageKind()
    {
        var ack = new TKMobileAckMsg
        {
            LordAckMsg = new LordAckMsg
            {
                Matchid = 888,
                LordwaitclientreadyAckMsg = new LordWaitClientReadyAck(),
            },
        };

        using var bodyStream = new MemoryStream();
        Serializer.Serialize(bodyStream, ack);
        var packet = ServerPacketFrame.EncodeFrame(1001, bodyStream.ToArray());

        var decoded = _codec.DecodePacket(
            packet,
            new ProtocolDecodeContext { Phase = ProtocolPhase.Game });

        Assert.Equal(ProtocolMessageKind.LordAck, decoded.Kind);
        Assert.Equal(888u, decoded.LordAcknowledgement?.Matchid);
    }

    [Fact]
    public void TryDecodeBody_EmptyBody_ReturnsEmptyBodyKind()
    {
        var ok = _codec.TryDecodeBody(
            header0: 0,
            bodyLength: 0,
            ReadOnlySpan<byte>.Empty,
            new ProtocolDecodeContext { Phase = ProtocolPhase.Signup },
            out var message,
            out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.NotNull(message);
        Assert.Equal(ProtocolMessageKind.EmptyBody, message!.Kind);
        Assert.Equal(0, message.BodyLength);
        Assert.Contains("EmptyBody", SessionMessageRouter.DescribeMessage(message), StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveMatchAckKind_MapsHighValueEnterRoundPushAcks()
    {
        const uint matchId = 475051269u;

        Assert.Equal(
            ProtocolMessageKind.BeginHandAck,
            DecodeMatchKind(matchId, ack => ack.BeginhandAckMsg = new BeginHandAck()));

        Assert.Equal(
            ProtocolMessageKind.RulerInfoAck,
            DecodeMatchKind(matchId, ack => ack.RulerinfoAckMsg = new RulerInfoAck()));

        Assert.Equal(
            ProtocolMessageKind.RulerInfoExAck,
            DecodeMatchKind(matchId, ack => ack.RulerinfoexAckMsg = new RulerInfoExAck()));

        Assert.Equal(
            ProtocolMessageKind.PushRoundRulerInfoAck,
            DecodeMatchKind(matchId, ack => ack.PushroundrulerinfoAckMsg = new PushRoundRulerInfoAck()));

        Assert.Equal(
            ProtocolMessageKind.StagePlayerOrderChangedAck,
            DecodeMatchKind(matchId, ack => ack.StageplayerorderchangedAckMsg = new StagePlayerOrderChangedAck()));

        Assert.Equal(
            ProtocolMessageKind.PushPlayerGameDataAck,
            DecodeMatchKind(matchId, ack => ack.PushplayergamedataAckMsg = new PushPlayerGameDataAck()));

        Assert.Equal(
            ProtocolMessageKind.PushMatchActionAck,
            DecodeMatchKind(matchId, ack => ack.PushmatchactionAckMsg = new PushMatchActionAck()));
    }

    private static ProtocolMessageKind DecodeMatchKind(uint matchId, Action<MatchAckMsg> configure)
    {
        var match = new MatchAckMsg { Matchid = matchId };
        configure(match);
        return MatchAckKindResolver.Resolve(match);
    }

    [Fact]
    public void DescribeMessage_UnknownAck_IncludesPopulatedSubMessages()
    {
        var ack = new TKMobileAckMsg
        {
            LobbyAckMsg = new LobbyAckMsg
            {
                PushuserwareAckMsg = new PushUserWareAck(),
            },
        };

        var description = SessionMessageRouter.DescribeMessage(new ProtocolMessage
        {
            Header0 = 1001,
            Kind = ProtocolMessageKind.Unknown,
            Acknowledgement = ack,
        });

        Assert.Contains("Unknown", description, StringComparison.Ordinal);
        Assert.Contains("ack=lobby=Pushuserware match=(empty) lord=(empty)", description, StringComparison.Ordinal);
    }

    [Fact]
    public void TryDecodeBody_InvalidProtobuf_ReportsStructuredError()
    {
        var ok = _codec.TryDecodeBody(
            header0: 5001,
            bodyLength: 4,
            new byte[] { 0xFF, 0xFE, 0xFD, 0xFC },
            new ProtocolDecodeContext
            {
                AccountAlias = "player3",
                Phase = ProtocolPhase.Login,
            },
            out _,
            out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Equal("player3", error!.AccountAlias);
        Assert.Equal(ProtocolPhase.Login, error.Phase);
        Assert.Equal(5001u, error.Header0);
        Assert.Equal(4, error.BodyLength);
        Assert.Equal(5001u, error.MessageId);
        Assert.Contains("Protobuf decode failed", error.Message, StringComparison.Ordinal);
    }
}
