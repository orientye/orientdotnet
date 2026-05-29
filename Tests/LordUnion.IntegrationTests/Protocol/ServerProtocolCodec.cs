using LordUnion.IntegrationTests.Protocol.Generated;
using Newtonsoft.Json;
using ProtoBuf;

namespace LordUnion.IntegrationTests.Protocol;

public sealed class ServerProtocolCodec
{
    public byte[] EncodeClientRequest<T>(T message)
    {
        using var bodyStream = new MemoryStream();
        Serializer.Serialize(bodyStream, message);
        return ServerPacketFrame.EncodeClientFrame(bodyStream.ToArray());
    }

    public ServerPacketFrame ReadFrameHeader(ReadOnlySpan<byte> headerBytes)
    {
        return ServerPacketFrame.DecodeHeader(headerBytes);
    }

    public bool TryReadFrameHeader(
        ReadOnlySpan<byte> headerBytes,
        ProtocolDecodeContext context,
        out ServerPacketFrame frame,
        out ProtocolDecodeError? error)
    {
        if (headerBytes.Length < ServerPacketFrame.HeaderLength)
        {
            frame = default;
            error = CreateError(
                context,
                header0: null,
                bodyLength: headerBytes.Length,
                messageId: null,
                $"Header must be at least {ServerPacketFrame.HeaderLength} bytes, got {headerBytes.Length}.");
            return false;
        }

        frame = ServerPacketFrame.DecodeHeader(headerBytes);
        if (frame.BodyLength < 0)
        {
            error = CreateError(
                context,
                frame.Header0,
                frame.BodyLength,
                frame.Header0,
                $"Invalid body length {frame.BodyLength}.");
            return false;
        }

        if (context.ExpectedHeader0 is uint expectedHeader0 && frame.Header0 != expectedHeader0)
        {
            error = CreateError(
                context,
                frame.Header0,
                frame.BodyLength,
                frame.Header0,
                $"Unexpected routing header0 {frame.Header0}, expected {expectedHeader0}.");
            return false;
        }

        error = null;
        return true;
    }

    public bool TryDecodePacket(
        ReadOnlySpan<byte> packet,
        ProtocolDecodeContext context,
        out ProtocolMessage? message,
        out ProtocolDecodeError? error)
    {
        message = null;
        if (!TryReadFrameHeader(packet, context, out var frame, out error))
        {
            return false;
        }

        if (packet.Length < ServerPacketFrame.HeaderLength + frame.BodyLength)
        {
            error = CreateError(
                context,
                frame.Header0,
                frame.BodyLength,
                frame.Header0,
                $"Packet truncated: need {ServerPacketFrame.HeaderLength + frame.BodyLength} bytes, got {packet.Length}.");
            return false;
        }

        var body = packet.Slice(ServerPacketFrame.HeaderLength, frame.BodyLength);
        return TryDecodeBody(frame.Header0, frame.BodyLength, body, context, out message, out error);
    }

    public bool TryDecodeBody(
        uint header0,
        int bodyLength,
        ReadOnlySpan<byte> bodyBytes,
        ProtocolDecodeContext context,
        out ProtocolMessage? message,
        out ProtocolDecodeError? error)
    {
        message = null;
        if (bodyLength == 0)
        {
            message = new ProtocolMessage
            {
                Header0 = header0,
                BodyLength = bodyLength,
                Kind = ProtocolMessageKind.EmptyBody,
            };
            error = null;
            return true;
        }

        try
        {
            if (IsClientRequestHeader(header0))
            {
                using var requestStream = new MemoryStream(bodyBytes.ToArray());
                var request = Serializer.Deserialize<TKMobileReqMsg>(requestStream);
                message = new ProtocolMessage
                {
                    Header0 = header0,
                    BodyLength = bodyLength,
                    Kind = IdentifyRequestKind(request),
                    Param = request.Param,
                    Request = request,
                };
            }
            else
            {
                using var ackStream = new MemoryStream(bodyBytes.ToArray());
                var ack = Serializer.Deserialize<TKMobileAckMsg>(ackStream);
                message = new ProtocolMessage
                {
                    Header0 = header0,
                    BodyLength = bodyLength,
                    Kind = IdentifyAckKind(ack),
                    Param = ack.Param,
                    Acknowledgement = ack,
                };
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = CreateError(
                context,
                header0,
                bodyLength,
                header0,
                $"Protobuf decode failed: {ex.Message}");
            return false;
        }
    }

    public ProtocolMessage DecodePacket(ReadOnlySpan<byte> packet, ProtocolDecodeContext? context = null)
    {
        if (!TryDecodePacket(packet, context ?? new ProtocolDecodeContext(), out var message, out var error))
        {
            throw new InvalidOperationException(error?.ToString() ?? "Decode failed.");
        }

        return message!;
    }

    public T DecodeBody<T>(ReadOnlySpan<byte> bodyBytes)
    {
        using var stream = new MemoryStream(bodyBytes.ToArray());
        return Serializer.Deserialize<T>(stream);
    }

    public TKMobileReqMsg CreateAnonymousBrowseRequest(uint serialId = LoginWireConstants.DefaultAnonymousSerialId)
    {
        return new TKMobileReqMsg
        {
            LobbyReqMsg = new LobbyReqMsg
            {
                AnonymousReqMsg = new AnonymousBrowseReq
                {
                    Serialid = serialId,
                },
            },
        };
    }

    public TKMobileReqMsg CreatePasswordLoginRequest(
        string username,
        string password,
        string aesKey,
        uint appId,
        long timestampMillis,
        uint loginTypeInContent = LoginWireConstants.LoginTypePassword,
        uint msgType = LoginWireConstants.GeneralPasswordLoginMsgType,
        int method = LoginWireConstants.GeneralPasswordLoginMethod)
    {
        var innerJson = LoginJsonBuilder.BuildPasswordLoginContent(username, password, loginTypeInContent);
        var outerJson = LoginJsonBuilder.BuildCommonLoginContent(method, appId, timestampMillis, innerJson);
        var encryptedJson = LobbyAes128Crypto.EncryptToHex(outerJson, aesKey);

        return new TKMobileReqMsg
        {
            LobbyReqMsg = new LobbyReqMsg
            {
                CommonloginReqMsg = new CommonLoginReq
                {
                    Jsondata = encryptedJson,
                    Cryptotype = 1,
                    Msgtype = msgType,
                },
            },
        };
    }

    public TKMobileReqMsg CreateTourneySignupRequest(
        uint userId,
        uint tourneyId,
        uint gameId,
        uint matchPoint,
        uint signupType = LoginWireConstants.DefaultSignupType,
        string? nickname = null)
    {
        return new TKMobileReqMsg
        {
            LobbyReqMsg = new LobbyReqMsg
            {
                TourneysignupReqMsg = new TourneySignupReq
                {
                    Userid = userId,
                    Nickname = nickname,
                    Tourneyid = tourneyId,
                    Signuptype = signupType,
                    Matchpoint = matchPoint,
                    Gameid = gameId,
                },
            },
        };
    }

    public TKMobileReqMsg CreateTourneyUnsignupRequest(
        uint userId,
        uint tourneyId,
        uint matchPoint,
        string? nickname = null,
        uint param = 0)
    {
        return new TKMobileReqMsg
        {
            LobbyReqMsg = new LobbyReqMsg
            {
                TourneyunsignupReqMsg = new TourneyUnsignupReq
                {
                    Userid = userId,
                    Nickname = nickname,
                    Tourneyid = tourneyId,
                    Matchpoint = matchPoint,
                    Param = param,
                },
            },
        };
    }

    public TKMobileReqMsg CreateExitGameRequest(uint matchId)
    {
        return new TKMobileReqMsg
        {
            MatchReqMsg = new MatchReqMsg
            {
                Matchid = matchId,
                ExitgameReqMsg = new ExitGameReq(),
            },
        };
    }

    public TKMobileReqMsg CreateExitMatchRequest(
        uint matchId,
        uint gameId,
        byte[]? ticket = null,
        uint matchClientVer = 0,
        uint gameClientVer = 0)
    {
        return new TKMobileReqMsg
        {
            MatchReqMsg = new MatchReqMsg
            {
                Matchid = matchId,
                ExitmatchReqMsg = new ExitMatchReq
                {
                    Gameid = gameId,
                    Ticket = ticket ?? Array.Empty<byte>(),
                    Matchclientver = matchClientVer,
                    Gameclientver = gameClientVer,
                },
            },
        };
    }

    public TKMobileReqMsg CreateEnterMatchRequest(
        uint matchId,
        uint gameId,
        byte[]? ticket = null,
        uint matchClientVer = 0,
        uint gameClientVer = 0)
    {
        return new TKMobileReqMsg
        {
            MatchReqMsg = new MatchReqMsg
            {
                Matchid = matchId,
                EntermatchReqMsg = new EnterMatchReq
                {
                    Gameid = gameId,
                    Matchclientver = matchClientVer,
                    Gameclientver = gameClientVer,
                    Ticket = ticket ?? Array.Empty<byte>(),
                },
            },
        };
    }

    public TKMobileReqMsg CreateEnterRoundRequest(
        uint matchId,
        uint gameId,
        byte[]? ticket = null,
        uint version = 0)
    {
        return new TKMobileReqMsg
        {
            MatchReqMsg = new MatchReqMsg
            {
                Matchid = matchId,
                EnterroundReqMsg = new EnterRoundReq
                {
                    Gameid = gameId,
                    Version = version,
                    Ticket = ticket ?? Array.Empty<byte>(),
                },
            },
        };
    }

    public TKMobileReqMsg CreateLordClientReadyRequest(uint matchId, uint seat)
    {
        return new TKMobileReqMsg
        {
            LordReqMsg = new LordReqMsg
            {
                Matchid = matchId,
                LordclientreadyReqMsg = new LordClientReadyReq
                {
                    Seat = seat,
                },
            },
        };
    }

    private static bool IsClientRequestHeader(uint header0) =>
        header0 == ServerPacketFrame.ClientSendHeaderMagic;

    private static ProtocolMessageKind IdentifyRequestKind(TKMobileReqMsg request)
    {
        if (request.LobbyReqMsg?.AnonymousReqMsg != null)
        {
            return ProtocolMessageKind.AnonymousBrowseReq;
        }

        if (request.LobbyReqMsg?.CommonloginReqMsg != null)
        {
            return ProtocolMessageKind.CommonLoginReq;
        }

        if (request.LobbyReqMsg?.TourneysignupReqMsg != null)
        {
            return ProtocolMessageKind.TourneySignupReq;
        }

        if (request.LobbyReqMsg?.TourneyunsignupReqMsg != null)
        {
            return ProtocolMessageKind.TourneyUnsignupReq;
        }

        if (request.MatchReqMsg?.EntermatchReqMsg != null)
        {
            return ProtocolMessageKind.EnterMatchReq;
        }

        if (request.MatchReqMsg?.EnterroundReqMsg != null)
        {
            return ProtocolMessageKind.EnterRoundReq;
        }

        if (request.MatchReqMsg?.ExitgameReqMsg != null)
        {
            return ProtocolMessageKind.ExitGameReq;
        }

        if (request.MatchReqMsg?.ExitmatchReqMsg != null)
        {
            return ProtocolMessageKind.ExitMatchReq;
        }

        if (request.LordReqMsg != null)
        {
            return ProtocolMessageKind.LordReq;
        }

        return ProtocolMessageKind.Unknown;
    }

    private static ProtocolMessageKind IdentifyAckKind(TKMobileAckMsg ack)
    {
        if (ack.LobbyAckMsg?.AnonymousAckMsg != null)
        {
            return ProtocolMessageKind.AnonymousBrowseAck;
        }

        if (ack.LobbyAckMsg?.CommonloginAckMsg != null)
        {
            return ProtocolMessageKind.CommonLoginAck;
        }

        if (ack.LobbyAckMsg?.TourneysignupexAckMsg != null)
        {
            return ProtocolMessageKind.TourneySignupAck;
        }

        if (ack.LobbyAckMsg?.TourneyunsignupAckMsg != null)
        {
            return ProtocolMessageKind.TourneyUnsignupAck;
        }

        if (ack.LobbyAckMsg?.StartclientexAckMsg != null)
        {
            return ProtocolMessageKind.StartClientExAck;
        }

        if (ack.LobbyAckMsg?.StartgameclientAckMsg != null)
        {
            return ProtocolMessageKind.StartGameClientAck;
        }

        if (ack.MatchAckMsg != null)
        {
            var matchKind = IdentifyMatchAckKind(ack.MatchAckMsg);
            if (matchKind != ProtocolMessageKind.Unknown)
            {
                return matchKind;
            }
        }

        if (ack.LordAckMsg != null)
        {
            return ProtocolMessageKind.LordAck;
        }

        return ProtocolMessageKind.Unknown;
    }

    private static ProtocolMessageKind IdentifyMatchAckKind(MatchAckMsg match) =>
        MatchAckKindResolver.Resolve(match);

    private static ProtocolDecodeError CreateError(
        ProtocolDecodeContext context,
        uint? header0,
        int? bodyLength,
        uint? messageId,
        string message)
    {
        return new ProtocolDecodeError
        {
            AccountAlias = context.AccountAlias,
            Phase = context.Phase,
            Header0 = header0,
            BodyLength = bodyLength,
            MessageId = messageId ?? header0,
            Message = message,
        };
    }
}

public static class LoginWireConstants
{
    public const uint GeneralPasswordLoginMsgType = 3588;
    public const int GeneralPasswordLoginMethod = 1003;
    public const uint LoginTypePassword = 2;
    public const uint DefaultAnonymousSerialId = 2000;
    public const uint DefaultAppId = 2;
    public const uint DefaultSignupType = 0;
}

public static class LoginJsonBuilder
{
    public static string BuildPasswordLoginContent(string username, string password, uint loginType)
    {
        var payload = new PasswordLoginContent
        {
            login_type = (int)loginType,
            login_name = username,
            password = password,
            password_type = 0,
            silder_token = string.Empty,
            silder_appid = string.Empty,
            slider_version = "3.0",
        };

        return JsonConvert.SerializeObject(payload);
    }

    public static string BuildCommonLoginContent(int method, uint appId, long timestampMillis, string contentJson)
    {
        var payload = new CommonLoginContent
        {
            method = method,
            version = "1.0",
            timestamp = timestampMillis,
            app_id = appId,
            content = contentJson,
        };

        return StandardizeJson(JsonConvert.SerializeObject(payload));
    }

    public static string StandardizeJson(string json)
    {
        return json
            .Replace("\\", string.Empty, StringComparison.Ordinal)
            .Replace("\"{", "{", StringComparison.Ordinal)
            .Replace("}\"", "}", StringComparison.Ordinal);
    }

    private sealed class PasswordLoginContent
    {
        public int login_type { get; set; }
        public string login_name { get; set; } = string.Empty;
        public string password { get; set; } = string.Empty;
        public int password_type { get; set; }
        public string silder_token { get; set; } = string.Empty;
        public string silder_appid { get; set; } = string.Empty;
        public string slider_version { get; set; } = string.Empty;
    }

    private sealed class CommonLoginContent
    {
        public int method { get; set; }
        public string version { get; set; } = string.Empty;
        public double timestamp { get; set; }
        public uint app_id { get; set; }
        public string content { get; set; } = string.Empty;
    }
}