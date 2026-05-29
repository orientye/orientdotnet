using System.Diagnostics;
using CRpc.Async;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Flows;
using LordUnion.IntegrationTests.Reporting;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Scenarios;

namespace LordUnion.IntegrationTests.Sessions;

public sealed class LordUnionSessionClient
{
    private readonly AccountSession session;
    private readonly IGameServerTransport transport;
    private readonly ServerProtocolCodec codec;
    private readonly EnterMatchFlow enterMatchFlow;
    private readonly EnterMatchFlowSessionState enterMatchState = new();
    private EnterMatchStartInfo? lastMatchStartInfo;

    public LordUnionSessionClient(
        AccountSession session,
        IGameServerTransport transport,
        ServerProtocolCodec codec,
        EnterMatchFlow? enterMatchFlow = null)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
        this.enterMatchFlow = enterMatchFlow ?? new EnterMatchFlow(codec);
    }

    public AccountSession Session => session;

    public string Alias => session.Alias;

    public async CRpcTask ConnectAsync(ServerConfig server, TimeSpan timeout)
    {
        EnsureOnLoopThread();
        ArgumentNullException.ThrowIfNull(server);

        session.SetState(AccountSessionState.Connecting);
        transport.BindIncomingHandler(session, codec);
        EnterMatchFlow.InstallMatchProgressCapture(session, enterMatchState);
        await transport.ConnectAsync(server, timeout, session.Loop);
        session.SetState(AccountSessionState.Connected);
    }

    public async CRpcTask<LoginStageResult> LoginAsync(
        AccountConfig account,
        ProtocolConfig protocol,
        TimeSpan timeout)
    {
        EnsureOnLoopThread();
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(protocol);

        var timeoutMs = ToTimeoutMilliseconds(timeout);

        try
        {
            var browseStopwatch = Stopwatch.StartNew();
            var (_, browseAck, browseMessage) = await CallAsync(
                codec.CreateAnonymousBrowseRequest(protocol.AnonymousSerialId),
                ProtocolMessageKind.AnonymousBrowseAck,
                static message => message.AnonymousBrowseAcknowledgement,
                timeoutMs);

            session.AnonymousRouteId = browseMessage.Header0;
            var aesKey = string.IsNullOrEmpty(browseAck.Param)
                ? LobbyAes128Crypto.DefaultKey
                : browseAck.Param!;
            session.AesKey = aesKey;

            var loginTimestampMillis = browseAck.ResolveLoginTimestampMillis(browseStopwatch.ElapsedMilliseconds);
            var (resultCode, commonLoginAck, loginMessage) = await CallAsync(
                codec.CreatePasswordLoginRequest(
                    account.Username,
                    account.Password,
                    aesKey,
                    protocol.AppId,
                    loginTimestampMillis,
                    protocol.LoginType),
                ProtocolMessageKind.CommonLoginAck,
                static message => message.CommonLoginAcknowledgement,
                timeoutMs);

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

            if (!success)
            {
                session.SetState(AccountSessionState.Failed);
                throw new InvalidOperationException(
                    $"[{session.Alias}] Login failed: {BuildLoginFailureMessage(loginMessage.Param, userId, decryptedLoginJson)}");
            }

            session.SetState(AccountSessionState.LoggedIn);
            return new LoginStageResult(
                resultCode,
                userId.GetValueOrDefault(),
                sessionId,
                nickname,
                aesKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (session.State != AccountSessionState.Failed)
            {
                session.SetState(AccountSessionState.Failed);
            }

            throw;
        }
    }

    public void ImportPostSignupMonitor(PostSignupDiagnosticMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        EnsureOnLoopThread();
        monitor.SeedFlowState(enterMatchState);
    }

    public async CRpcTask<SignupStageResult> SignupAsync(
        LordUnionGameProfile profile,
        TimeSpan timeout,
        bool allowUnsignupRetry = true)
    {
        EnsureOnLoopThread();
        ArgumentNullException.ThrowIfNull(profile);

        if (session.State != AccountSessionState.LoggedIn)
        {
            throw new InvalidOperationException(
                $"[{session.Alias}] Signup requires LoggedIn state; current state is {session.State}.");
        }

        if (session.UserId is not uint userId || userId == 0)
        {
            throw new InvalidOperationException(
                $"[{session.Alias}] Signup requires a non-zero UserId.");
        }

        try
        {
            var signup = await SignupOnceAsync(userId, profile, timeout);

            if (allowUnsignupRetry
                && signup.MobileResult != 0
                && !HasCapturedMatchStart())
            {
                await TryTourneyUnsignupAsync(userId, profile, timeout);
                signup = await SignupOnceAsync(userId, profile, timeout);
            }

            if (signup.MobileResult != 0 && !HasCapturedMatchStart())
            {
                session.SetState(AccountSessionState.Failed);
                throw new InvalidOperationException(
                    $"[{session.Alias}] Signup failed: mobile.param={signup.MobileResult} without StartGameClientAck or StartClientExAck.");
            }

            session.TourneyId = signup.TourneyId;
            session.MatchPoint = signup.MatchPoint;
            session.SetState(AccountSessionState.SignedUp);
            return signup;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (session.State != AccountSessionState.Failed)
            {
                session.SetState(AccountSessionState.Failed);
            }

            throw;
        }
    }

    private async CRpcTask<SignupStageResult> SignupOnceAsync(
        uint userId,
        LordUnionGameProfile profile,
        TimeSpan timeout)
    {
        var (mobileResult, signupAck, signupMessage) = await CallAsync(
            codec.CreateTourneySignupRequest(
                userId,
                profile.TourneyId,
                profile.GameId,
                matchPoint: profile.MatchPoint,
                nickname: session.Nickname),
            ProtocolMessageKind.TourneySignupAck,
            static message => message.TourneySignupAcknowledgement,
            ToTimeoutMilliseconds(timeout));

        enterMatchState.CaptureFromAnyMessage(signupMessage);

        if (signupAck.Param != 0)
        {
            session.SetState(AccountSessionState.Failed);
            throw new InvalidOperationException(
                $"[{session.Alias}] Signup failed: TourneySignupAck param={signupAck.Param}, mobile.param={mobileResult}.");
        }

        return new SignupStageResult(
            mobileResult,
            signupAck.Param,
            signupAck.Tourneyid,
            signupAck.Matchpoint,
            (uint)signupAck.Gameid);
    }

    private bool HasCapturedMatchStart() => enterMatchState.CapturedMatchStartMessage is not null;

    private async CRpcTask TryTourneyUnsignupAsync(
        uint userId,
        LordUnionGameProfile profile,
        TimeSpan timeout)
    {
        var timeoutMs = ToTimeoutMilliseconds(timeout);
        try
        {
            await SendRequestAsync(
                codec.CreateTourneyUnsignupRequest(
                    userId,
                    profile.TourneyId,
                    profile.MatchPoint,
                    session.Nickname));

            await session.WaitForMessageAsync(
                message => message.TourneyUnsignupAcknowledgement is not null,
                "TourneyUnsignupAck",
                timeoutMs);
        }
        catch (TimeoutException)
        {
        }
    }

    public async CRpcTask<MatchStartStageResult> WaitForMatchStartAsync(TimeSpan timeout)
    {
        EnsureOnLoopThread();

        var matchStart = await enterMatchFlow.WaitForMatchStartAsync(
            session,
            timeout,
            transport,
            enterMatchState);
        enterMatchFlow.ApplyMatchStartToSession(session, matchStart);
        lastMatchStartInfo = matchStart;
        var capturedStartClient = enterMatchState.CapturedMatchStartMessage?.StartClientExAcknowledgement;

        return new MatchStartStageResult(
            matchStart.MatchId,
            capturedStartClient?.Ip,
            capturedStartClient?.Port);
    }

    public async CRpcTask<EnterMatchStageResult> EnterMatchAsync(
        LordUnionGameProfile profile,
        MatchStartStageResult matchStart,
        TimeSpan timeout)
    {
        EnsureOnLoopThread();
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(matchStart);

        var startInfo = CreateStartInfo(profile, matchStart);
        await enterMatchFlow.EnterMatchOnlyAsync(
            session,
            startInfo,
            timeout,
            transport);

        return new EnterMatchStageResult(
            startInfo.MatchId,
            session.TableId);
    }

    public async CRpcTask<EnterRoundStageResult> EnterRoundAsync(
        LordUnionGameProfile profile,
        TimeSpan timeout)
    {
        EnsureOnLoopThread();
        ArgumentNullException.ThrowIfNull(profile);

        if (session.MatchId is not uint matchId || matchId == 0)
        {
            throw new InvalidOperationException($"[{session.Alias}] EnterRound requires a non-zero MatchId.");
        }

        var startInfo = new EnterMatchStartInfo
        {
            MatchId = matchId,
            GameId = profile.GameId,
            TourneyId = profile.TourneyId,
            MatchPoint = profile.MatchPoint,
            Ticket = session.Ticket ?? Array.Empty<byte>(),
        };

        var seat = await enterMatchFlow.EnterRoundOnlyAsync(
            session,
            startInfo,
            timeout,
            transport,
            enterMatchState);

        session.SeatOrder = seat;

        if (session.TableId is not uint tableId || tableId == 0)
        {
            tableId = matchId;
            session.TableId = tableId;
        }

        session.SetState(AccountSessionState.InGame);

        return new EnterRoundStageResult(matchId, tableId, seat);
    }

    private EnterMatchStartInfo CreateStartInfo(
        LordUnionGameProfile profile,
        MatchStartStageResult matchStart)
    {
        if (lastMatchStartInfo is { } cached && cached.MatchId == matchStart.MatchId)
        {
            return cached;
        }

        return new EnterMatchStartInfo
        {
            MatchId = matchStart.MatchId,
            GameId = profile.GameId,
            TourneyId = profile.TourneyId,
            MatchPoint = profile.MatchPoint,
            Ticket = session.Ticket ?? Array.Empty<byte>(),
        };
    }

    private async CRpcTask<(int Result, TAck Ack, ProtocolMessage Message)> CallAsync<TAck>(
        TKMobileReqMsg request,
        ProtocolMessageKind expectedKind,
        Func<ProtocolMessage, TAck?> getAck,
        int timeoutMs)
        where TAck : class
    {
        await SendRequestAsync(request);

        var message = await session.WaitForMessageAsync(expectedKind, timeoutMs);
        var ack = getAck(message);
        if (ack is null)
        {
            throw new InvalidOperationException(
                $"[{session.Alias}] {expectedKind} missing typed acknowledgement. ack.param={message.Param}.");
        }

        return ((int)message.Param, ack, message);
    }

    private async CRpcTask SendRequestAsync(TKMobileReqMsg request)
    {
        await session.SendRequestAsync(request);
        if (session.LastSentPacket is not null)
        {
            await transport.SendAsync(session.LastSentPacket, session.Loop);
        }
    }

    private static int ToTimeoutMilliseconds(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return 0;
        }

        return (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue);
    }

    private void EnsureOnLoopThread()
    {
        if (!session.Loop.IsInLoopThread)
        {
            throw new InvalidOperationException(
                $"LordUnionSessionClient for account '{session.Alias}' must run on the owner CRpcLoop thread.");
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

    private static string BuildLoginFailureMessage(uint loginErrorCode, uint? userId, string? decryptedLoginJson)
    {
        if (loginErrorCode != 0 && loginErrorCode != 31)
        {
            return $"CommonLoginAck param={loginErrorCode}. ackJson={decryptedLoginJson}";
        }

        if (loginErrorCode == 31)
        {
            return $"CommonLoginAck param=31 without a valid session id. ackJson={decryptedLoginJson}";
        }

        if (userId is null or 0)
        {
            return $"CommonLoginAck had no userid. ackJson={decryptedLoginJson}";
        }

        return "CommonLoginAck did not satisfy success conditions.";
    }
}