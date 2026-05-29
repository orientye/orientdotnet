using System.Net.Sockets;
using LordUnion.IntegrationTests.Protocol.Generated;
using ProtoBuf;

namespace LordUnion.IntegrationTests.Protocol;

public sealed class LoginWireSpikeOptions
{
    public string Host { get; init; } = "115.182.5.66";
    public int Port { get; init; } = 30301;
    public string Username { get; init; } = "TJJ006628";
    public string Password { get; init; } = "3YXRQW";
    public uint AppId { get; init; } = LoginWireConstants.DefaultAppId;
    public uint AnonymousSerialId { get; init; } = LoginWireConstants.DefaultAnonymousSerialId;
    public uint LoginTypeInContent { get; init; } = LoginWireConstants.LoginTypePassword;
    public int ConnectTimeoutMs { get; init; } = 10_000;
    public int ReadTimeoutMs { get; init; } = 10_000;
}

public sealed class LoginWireSpikeResult
{
    public bool Success { get; init; }
    public uint? UserId { get; init; }
    public uint LoginErrorCode { get; init; }
    public string? AesKey { get; init; }
    public string? Message { get; init; }
    public uint AnonymousRouteId { get; init; }
    public uint LoginRouteId { get; init; }
    public ulong ServerTimeMillisUsed { get; init; }

    public string? DecryptedLoginAckJson { get; init; }

    public ulong AnonymousU64ServerTime { get; init; }

    public uint? UserInfoUserId { get; init; }

    public uint CommonLoginAckMsgType { get; init; }

    public uint MinimalLoginAckParam { get; init; }

    public uint? MinimalUserInfoUserId { get; init; }
}

public sealed class LoginWireSpike
{
    private readonly ServerProtocolCodec _codec = new();

    public async Task<LoginWireSpikeResult> RunAsync(LoginWireSpikeOptions options,
        CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(options.ConnectTimeoutMs);
        await client.ConnectAsync(options.Host, options.Port, connectCts.Token);

        using var stream = client.GetStream();
        stream.ReadTimeout = options.ReadTimeoutMs;
        stream.WriteTimeout = options.ReadTimeoutMs;

        var anonymousPacket =
            _codec.EncodeClientRequest(_codec.CreateAnonymousBrowseRequest(options.AnonymousSerialId));
        await stream.WriteAsync(anonymousPacket, cancellationToken);

        var anonymousStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var anonymousFrame = await GameServerFrameReader.ReadFrameAsync(stream, cancellationToken);
        var anonymousMessage = _codec.DecodePacket(
            BuildPacket(anonymousFrame),
            new ProtocolDecodeContext { Phase = ProtocolPhase.AnonymousBrowse });
        var browseAck = anonymousMessage.AnonymousBrowseAcknowledgement;
        if (browseAck == null)
        {
            return new LoginWireSpikeResult
            {
                Success = false,
                AnonymousRouteId = anonymousFrame.Header0,
                Message = "AnonymousBrowseAck missing in server response.",
            };
        }

        var aesKey = string.IsNullOrEmpty(browseAck.Param) ? LobbyAes128Crypto.DefaultKey : browseAck.Param!;
        var loginTimestampMillis = browseAck.ResolveLoginTimestampMillis(anonymousStopwatch.ElapsedMilliseconds);
        var loginPacket = _codec.EncodeClientRequest(
            _codec.CreatePasswordLoginRequest(
                options.Username,
                options.Password,
                aesKey,
                options.AppId,
                loginTimestampMillis,
                options.LoginTypeInContent));

        await stream.WriteAsync(loginPacket, cancellationToken);

        var loginFrame = await GameServerFrameReader.ReadFrameAsync(stream, cancellationToken);
        var minimalLoginAck = DecodeMinimalLoginAck(loginFrame.Body);
        var loginMessage = _codec.DecodePacket(
            BuildPacket(loginFrame),
            new ProtocolDecodeContext { Phase = ProtocolPhase.Login });
        var commonLoginAck = loginMessage.CommonLoginAcknowledgement;
        if (commonLoginAck == null)
        {
            return new LoginWireSpikeResult
            {
                Success = false,
                AnonymousRouteId = anonymousFrame.Header0,
                LoginRouteId = loginFrame.Header0,
                AesKey = aesKey,
                LoginErrorCode = loginMessage.Param,
                Message = $"CommonLoginAck missing. ack.param={loginMessage.Param}",
            };
        }

        var decryptedLoginJson = commonLoginAck.Cryptotype == 1 && !string.IsNullOrEmpty(commonLoginAck.Jsondata)
            ? LobbyAes128Crypto.DecryptFromHex(commonLoginAck.Jsondata!, aesKey)
            : commonLoginAck.Jsondata;
        var userId = commonLoginAck.Userinfo?.Userid
                     ?? minimalLoginAck.LobbyAckMsg?.CommonLoginAckMsg?.UserInfo?.UserId
                     ?? LoginAckJsonParser.TryGetUserId(decryptedLoginJson);
        var sessionId = LoginAckJsonParser.TryGetSessionId(decryptedLoginJson);
        var success = userId is > 0 && (loginMessage.Param == 0 || (loginMessage.Param == 31 && sessionId is > 0));
        string? message = null;
        if (!success)
        {
            if (loginMessage.Param != 0)
            {
                message =
                    $"Login failed with error code {loginMessage.Param}. timestampMillis={loginTimestampMillis}, ackJson={decryptedLoginJson}";
            }
            else if (userId is null or 0)
            {
                message = $"Login ack had no userid. json={decryptedLoginJson}";
            }
        }
        else if (loginMessage.Param == 31)
        {
            message =
                $"Login returned param=31 with pid={userId} and sessionId={sessionId}; accepted for integration spike.";
        }

        return new LoginWireSpikeResult
        {
            Success = success,
            UserId = userId,
            LoginErrorCode = loginMessage.Param,
            AesKey = aesKey,
            AnonymousRouteId = anonymousFrame.Header0,
            LoginRouteId = loginFrame.Header0,
            ServerTimeMillisUsed = (ulong)loginTimestampMillis,
            Message = message,
            DecryptedLoginAckJson = decryptedLoginJson,
            AnonymousU64ServerTime = browseAck.U64servertime,
            UserInfoUserId = commonLoginAck.Userinfo?.Userid,
            CommonLoginAckMsgType = commonLoginAck.Msgtype,
            MinimalLoginAckParam = minimalLoginAck.Param,
            MinimalUserInfoUserId = minimalLoginAck.LobbyAckMsg?.CommonLoginAckMsg?.UserInfo?.UserId,
        };
    }

    private static byte[] BuildPacket(ServerPacketFrame frame)
    {
        return ServerPacketFrame.EncodeFrame(frame.Header0, frame.Body);
    }

    private static MinimalLoginAckBody DecodeMinimalLoginAck(byte[] body)
    {
        using var stream = new MemoryStream(body);
        return ProtoBuf.Serializer.Deserialize<MinimalLoginAckBody>(stream);
    }

    [ProtoContract]
    private sealed class MinimalLoginAckBody
    {
        [ProtoMember(1)] public uint Param { get; set; }

        [ProtoMember(2)] public MinimalLobbyAckBody? LobbyAckMsg { get; set; }
    }

    [ProtoContract]
    private sealed class MinimalLobbyAckBody
    {
        [ProtoMember(134)] public MinimalCommonLoginAckBody? CommonLoginAckMsg { get; set; }
    }

    [ProtoContract]
    private sealed class MinimalCommonLoginAckBody
    {
        [ProtoMember(1)] public string? JsonData { get; set; }

        [ProtoMember(2)] public uint CryptoType { get; set; }

        [ProtoMember(3)] public MinimalUserInfoBody? UserInfo { get; set; }

        [ProtoMember(4)] public uint MsgType { get; set; }
    }

    [ProtoContract]
    private sealed class MinimalUserInfoBody
    {
        [ProtoMember(1)] public uint UserId { get; set; }

        [ProtoMember(2)] public string Nickname { get; set; } = string.Empty;
    }
}