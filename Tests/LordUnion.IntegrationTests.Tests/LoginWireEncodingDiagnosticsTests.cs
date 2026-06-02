using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using ProtoBuf;

namespace LordUnion.IntegrationTests.Tests;

public class LoginWireEncodingDiagnosticsTests
{
    [Fact]
    public void GeneratedLoginRequest_MatchesHandwrittenWireEncoding()
    {
        var codec = new ServerProtocolCodec();
        var generatedPacket = codec.EncodeClientRequest(codec.CreatePasswordLoginRequest(
            "TJJ006628",
            "3YXRQW",
            LobbyAes128Crypto.DefaultKey,
            appId: 2,
            timestampMillis: 1_700_000_000_000));

        var handwritten = new Handwritten.TKMobileReqMsg
        {
            LobbyReqMsg = new Handwritten.LobbyReqMsg
            {
                CommonLoginReqMsg = new Handwritten.CommonLoginReq
                {
                    JsonData = codec.CreatePasswordLoginRequest(
                            "TJJ006628",
                            "3YXRQW",
                            LobbyAes128Crypto.DefaultKey,
                            appId: 2,
                            timestampMillis: 1_700_000_000_000)
                        .LobbyReqMsg!
                        .CommonloginReqMsg!
                        .Jsondata!,
                    CryptoType = 1,
                    MsgType = LoginWireConstants.GeneralPasswordLoginMsgType,
                },
            },
        };

        using var stream = new MemoryStream();
        Serializer.Serialize(stream, handwritten);
        var handwrittenBody = stream.ToArray();
        var generatedBody = generatedPacket.AsSpan(ServerPacketFrame.HeaderLength).ToArray();

        Assert.Equal(handwrittenBody, generatedBody);
    }

    [Fact]
    public void GeneratedAnonymousBrowseRequest_MatchesHandwrittenWireEncoding()
    {
        var codec = new ServerProtocolCodec();
        var generatedPacket = codec.EncodeClientRequest(codec.CreateAnonymousBrowseRequest(2000));

        var handwritten = new HandwrittenBrowse.TKMobileReqMsg
        {
            LobbyReqMsg = new HandwrittenBrowse.LobbyReqMsg
            {
                AnonymousReqMsg = new HandwrittenBrowse.AnonymousBrowseReq
                {
                    SerialId = 2000,
                },
            },
        };

        using var stream = new MemoryStream();
        Serializer.Serialize(stream, handwritten);
        var handwrittenBody = stream.ToArray();
        var generatedBody = generatedPacket.AsSpan(ServerPacketFrame.HeaderLength).ToArray();

        Assert.Equal(handwrittenBody, generatedBody);
    }

    private static class Handwritten
    {
        [ProtoContract]
        public sealed class TKMobileReqMsg
        {
            [ProtoMember(1)]
            public uint Param { get; set; }

            [ProtoMember(2)]
            public LobbyReqMsg? LobbyReqMsg { get; set; }
        }

        [ProtoContract]
        public sealed class LobbyReqMsg
        {
            [ProtoMember(134)]
            public CommonLoginReq? CommonLoginReqMsg { get; set; }
        }

        [ProtoContract]
        public sealed class CommonLoginReq
        {
            [ProtoMember(1)]
            public string? JsonData { get; set; }

            [ProtoMember(2)]
            public uint CryptoType { get; set; }

            [ProtoMember(3)]
            public uint MsgType { get; set; }
        }
    }

    private static class HandwrittenBrowse
    {
        [ProtoContract]
        public sealed class TKMobileReqMsg
        {
            [ProtoMember(2)]
            public LobbyReqMsg? LobbyReqMsg { get; set; }
        }

        [ProtoContract]
        public sealed class LobbyReqMsg
        {
            [ProtoMember(3)]
            public AnonymousBrowseReq? AnonymousReqMsg { get; set; }
        }

        [ProtoContract]
        public sealed class AnonymousBrowseReq
        {
            [ProtoMember(4)]
            public uint SerialId { get; set; }
        }
    }
}
