using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using ProtoBuf;

namespace LordUnion.IntegrationTests.Tests;

public class ServerProtocolCodecSpikeTests
{
    [Fact]
    public void DecodeHeader_ReturnsExpectedBodyLengthAndRouteId()
    {
        var body = new byte[] { 1, 2, 3, 4, 5 };
        var packet = ServerPacketFrame.EncodeFrame(1001, body);

        var header = ServerPacketFrame.DecodeHeader(packet.AsSpan(0, ServerPacketFrame.HeaderLength));

        Assert.Equal(1001u, header.Header0);
        Assert.Equal(body.Length, header.BodyLength);
    }

    [Fact]
    public void EncodeClientRequest_Writes14801HeaderAndProtobufBody()
    {
        var codec = new ServerProtocolCodec();
        var request = codec.CreateAnonymousBrowseRequest(serialId: 42);

        var packet = codec.EncodeClientRequest(request);
        var header = ServerPacketFrame.DecodeHeader(packet.AsSpan(0, ServerPacketFrame.HeaderLength));

        Assert.Equal(ServerPacketFrame.ClientSendHeaderMagic, header.Header0);
        Assert.Equal(packet.Length - ServerPacketFrame.HeaderLength, header.BodyLength);

        using var bodyStream = new MemoryStream(packet, ServerPacketFrame.HeaderLength, header.BodyLength, writable: false);
        var decoded = Serializer.Deserialize<TKMobileReqMsg>(bodyStream);

        Assert.NotNull(decoded.LobbyReqMsg?.AnonymousReqMsg);
        Assert.Equal(42u, decoded.LobbyReqMsg.AnonymousReqMsg.Serialid);
    }

    [Fact]
    public void RoundTrip_AnonymousBrowseRequest_PreservesSerialId()
    {
        var codec = new ServerProtocolCodec();
        var packet = codec.EncodeClientRequest(codec.CreateAnonymousBrowseRequest(99));
        var header = codec.ReadFrameHeader(packet.AsSpan(0, ServerPacketFrame.HeaderLength));
        var decoded = codec.DecodeBody<TKMobileReqMsg>(packet.AsSpan(ServerPacketFrame.HeaderLength, header.BodyLength));

        Assert.Equal(99u, decoded.LobbyReqMsg?.AnonymousReqMsg?.Serialid);
    }

    [Fact]
    public void LoginJsonBuilder_UsesServerMillisTimestamp()
    {
        const long timestampMillis = 1_779_693_696_000;
        var outer = LoginJsonBuilder.BuildCommonLoginContent(
            LoginWireConstants.GeneralPasswordLoginMethod,
            appId: 2,
            timestampMillis,
            "{\"login_type\":2}");

        Assert.Contains("1779693696000", outer, StringComparison.Ordinal);
    }

    [Fact]
    public void AnonymousBrowseAck_ResolveLoginTimestampMillis_PrefersU64ServerTime()
    {
        var ack = new AnonymousBrowseAck
        {
            Servertime = 1_779_693_696,
            U64servertime = 1_779_693_696_815,
        };

        Assert.Equal(1_779_693_696_815, ack.ResolveLoginTimestampMillis());
    }

    [Fact]
    public void AnonymousBrowseAck_ResolveLoginTimestampMillis_FallsBackToServerTimeSeconds()
    {
        var ack = new AnonymousBrowseAck
        {
            Servertime = 1_779_693_696,
        };

        Assert.Equal(1_779_693_696_000, ack.ResolveLoginTimestampMillis());
    }

    [Fact]
    public void LoginJsonBuilder_StandardizeJson_UnwrapsNestedContentObject()
    {
        var inner = LoginJsonBuilder.BuildPasswordLoginContent("user", "pass", loginType: 1);
        var outer = LoginJsonBuilder.BuildCommonLoginContent(
            LoginWireConstants.GeneralPasswordLoginMethod,
            appId: 1,
            timestampMillis: 1_700_000_000_000,
            inner);

        Assert.Contains("\"content\":{", outer, StringComparison.Ordinal);
        Assert.DoesNotContain("\"content\":\"{", outer, StringComparison.Ordinal);
    }

    [Fact]
    public void LobbyAes128Crypto_RoundTripsKnownPlainText()
    {
        const string plain = "{\"pid\":106144882,\"reg_flag\":0}";
        var cipher = LobbyAes128Crypto.EncryptToHex(plain, LobbyAes128Crypto.DefaultKey);
        var decrypted = LobbyAes128Crypto.DecryptFromHex(cipher, LobbyAes128Crypto.DefaultKey);

        Assert.Equal(plain, decrypted);
    }

    [Fact]
    public void CreatePasswordLoginRequest_EncodesCommonLoginFields()
    {
        var codec = new ServerProtocolCodec();
        var request = codec.CreatePasswordLoginRequest(
            "TJJ006628",
            "3YXRQW",
            LobbyAes128Crypto.DefaultKey,
            appId: 0,
            timestampMillis: 1_700_000_000_000);

        Assert.NotNull(request.LobbyReqMsg?.CommonloginReqMsg);
        Assert.Equal(1u, request.LobbyReqMsg.CommonloginReqMsg.Cryptotype);
        Assert.Equal(LoginWireConstants.GeneralPasswordLoginMsgType, request.LobbyReqMsg.CommonloginReqMsg.Msgtype);
        Assert.False(string.IsNullOrWhiteSpace(request.LobbyReqMsg.CommonloginReqMsg.Jsondata));
    }

    [Fact]
    public void ProductionCodec_DecodePacket_MatchesSpikeRoundTrip()
    {
        var codec = new ServerProtocolCodec();
        var packet = codec.EncodeClientRequest(codec.CreateAnonymousBrowseRequest(1234));
        var decoded = codec.DecodePacket(packet);

        Assert.Equal(ProtocolMessageKind.AnonymousBrowseReq, decoded.Kind);
        Assert.Equal(1234u, decoded.AnonymousBrowseRequest?.Serialid);
    }
}
