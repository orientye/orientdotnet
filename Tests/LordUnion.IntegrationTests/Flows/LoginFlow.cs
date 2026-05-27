using System.Diagnostics;
using CRpc.Async;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Sessions;

namespace LordUnion.IntegrationTests.Flows;

public sealed class LoginFlow
{
    private readonly ServerProtocolCodec codec;

    public LoginFlow(ServerProtocolCodec? codec = null)
    {
        this.codec = codec ?? new ServerProtocolCodec();
    }

    public CRpcTask<LoginFlowResult> RunAsync(
        AccountSession session,
        AccountConfig account,
        ServerConfig server,
        ProtocolConfig protocol,
        TimeSpan loginTimeout,
        IGameServerTransport? transport = null)
    {
        return RunCoreAsync(session, account, server, protocol, loginTimeout, transport);
    }

    private async CRpcTask<LoginFlowResult> RunCoreAsync(
        AccountSession session,
        AccountConfig account,
        ServerConfig server,
        ProtocolConfig protocol,
        TimeSpan loginTimeout,
        IGameServerTransport? transport)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(protocol);
        EnsureOnLoopThread(session);

        var timeoutMs = ToTimeoutMilliseconds(loginTimeout);
        transport?.BindIncomingHandler(session, codec);

        try
        {
            session.SetState(AccountSessionState.Connecting);

            if (transport is not null)
            {
                await transport.ConnectAsync(server, loginTimeout, session.Loop);
            }

            session.SetState(AccountSessionState.Connected);

            var browseStopwatch = Stopwatch.StartNew();
            await SendRequestAsync(session, transport, codec.CreateAnonymousBrowseRequest(protocol.AnonymousSerialId));

            var browseMessage = await session.WaitForMessageAsync(
                ProtocolMessageKind.AnonymousBrowseAck,
                timeoutMs);
            var browseAck = browseMessage.AnonymousBrowseAcknowledgement
                ?? throw new InvalidOperationException("AnonymousBrowseAck missing in server response.");

            session.AnonymousRouteId = browseMessage.Header0;
            var aesKey = string.IsNullOrEmpty(browseAck.Param) ? LobbyAes128Crypto.DefaultKey : browseAck.Param!;
            session.AesKey = aesKey;

            var loginTimestampMillis = browseAck.ResolveLoginTimestampMillis(browseStopwatch.ElapsedMilliseconds);
            await SendRequestAsync(
                session,
                transport,
                codec.CreatePasswordLoginRequest(
                    account.Username,
                    account.Password,
                    aesKey,
                    protocol.AppId,
                    loginTimestampMillis,
                    protocol.LoginType));

            var loginMessage = await session.WaitForMessageAsync(
                ProtocolMessageKind.CommonLoginAck,
                timeoutMs);
            var commonLoginAck = loginMessage.CommonLoginAcknowledgement
                ?? throw new InvalidOperationException(
                    $"CommonLoginAck missing. ack.param={loginMessage.Param}.");

            session.LoginRouteId = loginMessage.Header0;
            var decryptedLoginJson = DecryptLoginAckJson(commonLoginAck, aesKey);
            var userId = commonLoginAck.Userinfo?.Userid
                ?? LoginAckJsonParser.TryGetUserId(decryptedLoginJson);
            var sessionId = LoginAckJsonParser.TryGetSessionId(decryptedLoginJson);
            var nickname = commonLoginAck.Userinfo?.Nickname;
            var success = userId is > 0
                && (loginMessage.Param == 0 || (loginMessage.Param == 31 && sessionId is > 0));

            session.UserId = userId;
            session.Nickname = nickname;
            session.SessionId = sessionId;
            session.LoginErrorCode = loginMessage.Param;

            var result = new LoginFlowResult
            {
                Success = success,
                UserId = userId,
                Nickname = nickname,
                AesKey = aesKey,
                SessionId = sessionId,
                LoginErrorCode = loginMessage.Param,
                AnonymousRouteId = browseMessage.Header0,
                LoginRouteId = loginMessage.Header0,
                DecryptedLoginAckJson = decryptedLoginJson,
                FailureMessage = success ? null : BuildFailureMessage(loginMessage.Param, userId, decryptedLoginJson),
            };

            session.SetState(success ? AccountSessionState.LoggedIn : AccountSessionState.Failed);
            return result;
        }
        catch (TimeoutException)
        {
            session.SetState(AccountSessionState.Failed);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            session.SetState(AccountSessionState.Failed);
            throw;
        }
    }

    private static async CRpcTask SendRequestAsync(
        AccountSession session,
        IGameServerTransport? transport,
        TKMobileReqMsg request)
    {
        await session.SendRequestAsync(request);
        if (transport is not null && session.LastSentPacket is not null)
        {
            await transport.SendAsync(session.LastSentPacket, session.Loop);
        }
    }

    private static string? DecryptLoginAckJson(CommonLoginAck commonLoginAck, string aesKey)
    {
        if (commonLoginAck.Cryptotype == 1 && !string.IsNullOrEmpty(commonLoginAck.Jsondata))
        {
            return LobbyAes128Crypto.DecryptFromHex(commonLoginAck.Jsondata!, aesKey);
        }

        return commonLoginAck.Jsondata;
    }

    private static string BuildFailureMessage(uint loginErrorCode, uint? userId, string? decryptedLoginJson)
    {
        if (loginErrorCode != 0 && loginErrorCode != 31)
        {
            return $"Login failed with error code {loginErrorCode}. ackJson={decryptedLoginJson}";
        }

        if (loginErrorCode == 31)
        {
            return $"Login returned param=31 without a valid session id. ackJson={decryptedLoginJson}";
        }

        if (userId is null or 0)
        {
            return $"Login ack had no userid. ackJson={decryptedLoginJson}";
        }

        return "Login failed.";
    }

    private static int ToTimeoutMilliseconds(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return 0;
        }

        return (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue);
    }

    private static void EnsureOnLoopThread(AccountSession session)
    {
        if (!session.Loop.IsInLoopThread)
        {
            throw new InvalidOperationException("LoginFlow must run on the account session CRpcLoop thread.");
        }
    }
}
