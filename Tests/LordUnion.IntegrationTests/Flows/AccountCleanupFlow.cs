using CRpc.Async;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Sessions;

namespace LordUnion.IntegrationTests.Flows;

/// <summary>
/// Best-effort pre-signup cleanup: tourney unsignup plus exit from any discovered active match.
/// Failures are recorded but do not fail the scenario.
/// </summary>
internal sealed class AccountCleanupFlow
{
    private const int UnsignupWaitMs = 5000;
    private const int ExitStepWaitMs = 3000;
    private const int PostCleanupSettleMs = 3000;

    private readonly ServerProtocolCodec codec;

    public AccountCleanupFlow(ServerProtocolCodec? codec = null)
    {
        this.codec = codec ?? new ServerProtocolCodec();
    }

    public CRpcTask<AccountCleanupFlowResult> RunAsync(
        AccountSession session,
        MatchConfig match,
        IGameServerTransport? transport = null,
        int drainWindowMs = AccountCleanupRunOptions.DefaultDrainWindowMs)
    {
        return RunAsync(session, match, transport, AccountCleanupRunOptions.PreSignup(drainWindowMs));
    }

    public CRpcTask<AccountCleanupFlowResult> RunAsync(
        AccountSession session,
        MatchConfig match,
        IGameServerTransport? transport,
        AccountCleanupRunOptions options)
    {
        return RunCoreAsync(session, match, transport, options);
    }

    private async CRpcTask<AccountCleanupFlowResult> RunCoreAsync(
        AccountSession session,
        MatchConfig match,
        IGameServerTransport? transport,
        AccountCleanupRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(options);
        EnsureOnLoopThread(session);

        if (!IsAllowedSessionState(session.State, options.PostGame))
        {
            throw new InvalidOperationException(
                $"AccountCleanupFlow requires a valid session state for '{session.Alias}'; current state is {session.State}, postGame={options.PostGame}.");
        }

        if (session.UserId is not uint userId || userId == 0)
        {
            throw new InvalidOperationException(
                $"AccountCleanupFlow requires session '{session.Alias}' to have a non-zero UserId.");
        }

        transport?.BindIncomingHandler(session, codec);

        var discoveredMatchIds = new HashSet<uint>();
        var exitGameAttempted = new List<uint>();
        var exitMatchAttempted = new List<uint>();
        var unsignupSent = false;
        var unsignupAckReceived = false;
        uint? unsignupParam = null;

        var previousPushHandler = session.PushMessageReceived;
        session.PushMessageReceived = message =>
        {
            previousPushHandler?.Invoke(message);
            CaptureMatchIds(message, discoveredMatchIds);
        };

        foreach (var matchId in options.KnownMatchIds)
        {
            if (matchId > 0)
            {
                discoveredMatchIds.Add(matchId);
            }
        }

        try
        {
            if (options.DrainWindowMs > 0)
            {
                await CRpcTask.Delay(options.DrainWindowMs, session.Loop);
            }

            await SendRequestAsync(
                session,
                transport,
                codec.CreateTourneyUnsignupRequest(
                    userId,
                    match.TourneyId,
                    match.ProductId,
                    session.Nickname));
            unsignupSent = true;

            try
            {
                var unsignupMessage = await session.WaitForMessageAsync(
                    message => message.TourneyUnsignupAcknowledgement is not null,
                    "TourneyUnsignupAck",
                    UnsignupWaitMs);
                unsignupAckReceived = true;
                unsignupParam = unsignupMessage.TourneyUnsignupAcknowledgement?.Param;
            }
            catch (TimeoutException)
            {
            }

            foreach (var matchId in discoveredMatchIds.ToList())
            {
                await TryExitGameAsync(session, transport, matchId, exitGameAttempted);
                await TryExitMatchAsync(session, transport, match, matchId, exitMatchAttempted);
            }

            if (PostCleanupSettleMs > 0)
            {
                await CRpcTask.Delay(PostCleanupSettleMs, session.Loop);
            }
        }
        finally
        {
            session.PushMessageReceived = previousPushHandler;
        }

        return new AccountCleanupFlowResult
        {
            UnsignupSent = unsignupSent,
            UnsignupAckReceived = unsignupAckReceived,
            UnsignupParam = unsignupParam,
            DiscoveredMatchIds = discoveredMatchIds.ToList(),
            ExitGameAttemptedMatchIds = exitGameAttempted,
            ExitMatchAttemptedMatchIds = exitMatchAttempted,
        };
    }

    private async CRpcTask TryExitGameAsync(
        AccountSession session,
        IGameServerTransport? transport,
        uint matchId,
        List<uint> attempted)
    {
        attempted.Add(matchId);

        await SendRequestAsync(session, transport, codec.CreateExitGameRequest(matchId));

        try
        {
            await session.WaitForMessageAsync(
                message => message.ExitGameAcknowledgement is not null
                           && message.Acknowledgement?.MatchAckMsg?.Matchid == matchId,
                "ExitGameAck",
                ExitStepWaitMs);
        }
        catch (TimeoutException)
        {
        }
    }

    private async CRpcTask TryExitMatchAsync(
        AccountSession session,
        IGameServerTransport? transport,
        MatchConfig match,
        uint matchId,
        List<uint> attempted)
    {
        attempted.Add(matchId);

        await SendRequestAsync(
            session,
            transport,
            codec.CreateExitMatchRequest(matchId, match.GameId, session.Ticket));

        try
        {
            await session.WaitForMessageAsync(
                message => message.Acknowledgement?.MatchAckMsg?.Matchid == matchId
                           && message.Kind != ProtocolMessageKind.Unknown,
                "ExitMatchResponse",
                ExitStepWaitMs);
        }
        catch (TimeoutException)
        {
        }
    }

    public static void CaptureMatchIds(ProtocolMessage message, ISet<uint> discoveredMatchIds)
    {
        if (message.StartClientExAcknowledgement?.Matchid is uint startClientMatchId and > 0)
        {
            discoveredMatchIds.Add(startClientMatchId);
        }

        if (message.StartGameClientAcknowledgement?.Matchid is uint startGameMatchId and > 0)
        {
            discoveredMatchIds.Add(startGameMatchId);
        }

        if (message.LordAcknowledgement?.Matchid is uint lordMatchId and > 0)
        {
            discoveredMatchIds.Add(lordMatchId);
        }

        if (message.Acknowledgement?.MatchAckMsg?.Matchid is uint matchAckMatchId and > 0)
        {
            discoveredMatchIds.Add(matchAckMatchId);
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

    private static bool IsAllowedSessionState(AccountSessionState state, bool postGame)
    {
        if (state == AccountSessionState.LoggedIn)
        {
            return true;
        }

        return postGame
               && state is AccountSessionState.Finished
                   or AccountSessionState.InGame
                   or AccountSessionState.SignedUp;
    }

    private static void EnsureOnLoopThread(AccountSession session)
    {
        if (!session.Loop.IsInLoopThread)
        {
            throw new InvalidOperationException("AccountCleanupFlow must run on the account session CRpcLoop thread.");
        }
    }
}