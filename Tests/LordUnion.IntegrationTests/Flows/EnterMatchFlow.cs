using System.Text;
using CRpc.Async;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Reporting;
using LordUnion.IntegrationTests.Sessions;

namespace LordUnion.IntegrationTests.Flows;

public sealed class EnterMatchFlow
{
    private static readonly ProtocolMessageKind[] MatchStartAckKinds =
    [
        ProtocolMessageKind.StartGameClientAck,
        ProtocolMessageKind.StartClientExAck,
    ];

    private readonly ServerProtocolCodec codec;

    public EnterMatchFlow(ServerProtocolCodec? codec = null)
    {
        this.codec = codec ?? new ServerProtocolCodec();
    }

    public CRpcTask<EnterMatchFlowResult> RunAsync(
        AccountSession session,
        MatchConfig match,
        TimeSpan matchStartTimeout,
        TimeSpan enterMatchTimeout,
        TimeSpan enterRoundTimeout,
        IGameServerTransport? transport = null)
    {
        _ = match;
        var state = new EnterMatchFlowSessionState();
        return RunCoreAsync(
            session,
            matchStartTimeout,
            enterMatchTimeout,
            enterRoundTimeout,
            transport,
            state);
    }

    public CRpcTask<EnterMatchStartInfo> WaitForMatchStartAsync(
        AccountSession session,
        TimeSpan matchStartTimeout,
        IGameServerTransport? transport = null,
        EnterMatchFlowSessionState? state = null)
    {
        EnsureSignedUp(session);
        transport?.BindIncomingHandler(session, codec);
        var flowState = state ?? new EnterMatchFlowSessionState();
        InstallMatchProgressCapture(session, flowState);

        return WaitForMatchStartCoreAsync(
            session,
            ToTimeoutMilliseconds(matchStartTimeout),
            flowState);
    }

    public void ApplyMatchStartToSession(AccountSession session, EnterMatchStartInfo matchStart)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(matchStart);

        session.MatchId = matchStart.MatchId;
        session.Ticket = matchStart.Ticket;
        session.TourneyId = matchStart.TourneyId;
        session.MatchPoint = matchStart.MatchPoint;
        session.TableId = matchStart.MatchId;
    }

    public async CRpcTask<bool> EnterMatchOnlyAsync(
        AccountSession session,
        EnterMatchStartInfo matchStart,
        TimeSpan enterMatchTimeout,
        IGameServerTransport? transport = null)
    {
        ArgumentNullException.ThrowIfNull(matchStart);
        EnsureSignedUpUserId(session, out _);

        try
        {
            ApplyMatchStartToSession(session, matchStart);
            session.SetState(AccountSessionState.EnteringMatch);

            await SendRequestAsync(
                session,
                transport,
                codec.CreateEnterMatchRequest(matchStart.MatchId, matchStart.GameId, matchStart.Ticket));

            var enterMatchMessage = await session.WaitForMessageAsync(
                ProtocolMessageKind.EnterMatchAck,
                ToTimeoutMilliseconds(enterMatchTimeout));
            var enterMatchAck = enterMatchMessage.EnterMatchAcknowledgement
                                ?? throw new InvalidOperationException("EnterMatchAck missing in server response.");

            if (enterMatchAck.Matchid != matchStart.MatchId)
            {
                throw new InvalidOperationException(
                    $"EnterMatchAck matchid {enterMatchAck.Matchid} did not match match start matchid {matchStart.MatchId}.");
            }

            return true;
        }
        catch (TimeoutException)
        {
            session.SetState(AccountSessionState.Failed);
            throw;
        }
    }

    public async CRpcTask<uint> EnterRoundOnlyAsync(
        AccountSession session,
        EnterMatchStartInfo matchStart,
        TimeSpan enterRoundTimeout,
        IGameServerTransport? transport = null,
        EnterMatchFlowSessionState? state = null)
    {
        ArgumentNullException.ThrowIfNull(matchStart);
        EnsureSignedUpUserId(session, out var userId);

        session.SetState(AccountSessionState.EnteringMatch);

        try
        {
            await SendRequestAsync(
                session,
                transport,
                codec.CreateEnterRoundRequest(matchStart.MatchId, matchStart.GameId, matchStart.Ticket));

            var resolvedSeat = ResolveSeatForUser(userId, state, enterRoundAck: null);
            if (resolvedSeat is uint seatOrder)
            {
                session.SeatOrder = seatOrder;
                return seatOrder;
            }

            var deadlineMs = ToTimeoutMilliseconds(enterRoundTimeout);
            var startedAt = Environment.TickCount64;

            while (resolvedSeat is null)
            {
                resolvedSeat = ResolveSeatForUser(userId, state, state?.LastEnterRoundAck);
                if (resolvedSeat is uint)
                {
                    break;
                }

                var remainingMs = deadlineMs - (int)(Environment.TickCount64 - startedAt);
                if (remainingMs <= 0)
                {
                    throw new TimeoutException(
                        $"Timed out waiting for EnterRound completion on account '{session.Alias}' in state {session.State} (phase {session.CurrentPhase}).");
                }

                var message = await session.WaitForMessageAsync(
                    IsEnterRoundProgressMessage,
                    "EnterRoundAck, InitGameTableAck, AddGamePlayerInfoAck, or LordAck",
                    remainingMs);

                state?.CaptureFromAnyMessage(message);

                resolvedSeat = ResolveSeatForUser(
                    userId,
                    state,
                    message.EnterRoundAcknowledgement ?? state?.LastEnterRoundAck);

                if (IsEnterRoundComplete(message, resolvedSeat, state, userId))
                {
                    resolvedSeat = ResolveSeatForUser(userId, state, state?.LastEnterRoundAck);
                    if (resolvedSeat is uint readySeat
                        && message.Kind == ProtocolMessageKind.LordAck
                        && LordAckDescriber.IsGameReadySignal(message.LordAcknowledgement))
                    {
                        await SendRequestAsync(
                            session,
                            transport,
                            codec.CreateLordClientReadyRequest(matchStart.MatchId, readySeat));
                    }

                    break;
                }

                // Burst messages may still be queued on the loop as push deliveries.
                await CRpcTask.Delay(1, session.Loop);
                resolvedSeat = ResolveSeatForUser(userId, state, state?.LastEnterRoundAck);
                if (resolvedSeat is uint)
                {
                    break;
                }
            }

            session.SeatOrder = resolvedSeat!.Value;
            return resolvedSeat.Value;
        }
        catch (TimeoutException)
        {
            session.SetState(AccountSessionState.Failed);
            throw;
        }
    }

    public EnterMatchFlowResult CreateSuccessResult(
        AccountSession session,
        EnterMatchStartInfo matchStart,
        uint seatOrder,
        EnterMatchFlowSessionState? state = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(matchStart);
        EnsureSignedUpUserId(session, out var userId);

        var tableId = ResolveTableId(matchStart.MatchId, state?.InitGameTableAck);
        session.TableId = tableId;
        session.SetState(AccountSessionState.InGame);

        return new EnterMatchFlowResult
        {
            Success = true,
            UserId = userId,
            MatchId = matchStart.MatchId,
            Ticket = matchStart.Ticket,
            TourneyId = matchStart.TourneyId,
            MatchPoint = matchStart.MatchPoint,
            GameId = matchStart.GameId,
            TableId = tableId,
            SeatOrder = seatOrder,
            SeatUserMapping = BuildSeatUserMapping(state?.InitGameTableAck),
        };
    }

    public CRpcTask<EnterMatchFlowResult> EnterTableAsync(
        AccountSession session,
        EnterMatchStartInfo matchStart,
        TimeSpan enterMatchTimeout,
        TimeSpan enterRoundTimeout,
        IGameServerTransport? transport = null,
        EnterMatchFlowSessionState? state = null)
    {
        ArgumentNullException.ThrowIfNull(matchStart);
        EnsureSignedUpUserId(session, out var userId);

        var flowState = state ?? new EnterMatchFlowSessionState();
        return EnterTableCoreAsync(
            session,
            userId,
            matchStart,
            ToTimeoutMilliseconds(enterMatchTimeout),
            ToTimeoutMilliseconds(enterRoundTimeout),
            transport,
            flowState);
    }

    private async CRpcTask<EnterMatchFlowResult> RunCoreAsync(
        AccountSession session,
        TimeSpan matchStartTimeout,
        TimeSpan enterMatchTimeout,
        TimeSpan enterRoundTimeout,
        IGameServerTransport? transport,
        EnterMatchFlowSessionState state)
    {
        var matchStart = await WaitForMatchStartAsync(session, matchStartTimeout, transport, state);
        await EnterMatchOnlyAsync(session, matchStart, enterMatchTimeout, transport);
        var seatOrder = await EnterRoundOnlyAsync(session, matchStart, enterRoundTimeout, transport, state);
        return CreateSuccessResult(session, matchStart, seatOrder, state);
    }

    private async CRpcTask<EnterMatchStartInfo> WaitForMatchStartCoreAsync(
        AccountSession session,
        int matchStartTimeoutMs,
        EnterMatchFlowSessionState state)
    {
        try
        {
            session.SetState(AccountSessionState.WaitingForMatch);

            var matchStart = TryParseCapturedMatchStart(state);
            if (matchStart is not null)
            {
                return matchStart;
            }

            var startedAt = Environment.TickCount64;
            while (matchStart is null)
            {
                matchStart = TryParseCapturedMatchStart(state);
                if (matchStart is not null)
                {
                    break;
                }

                var remainingMs = matchStartTimeoutMs - (int)(Environment.TickCount64 - startedAt);
                if (remainingMs <= 0)
                {
                    throw CreateMatchStartTimeoutException(session);
                }

                var message = await session.WaitForMessageAsync(
                    MatchStartAckKinds,
                    remainingMs);
                state.CaptureFromAnyMessage(message);
                matchStart = TryParseCapturedMatchStart(state);
            }

            return matchStart!;
        }
        catch (TimeoutException ex)
        {
            session.SetState(AccountSessionState.Failed);
            throw CreateMatchStartTimeoutException(session, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            session.SetState(AccountSessionState.Failed);
            throw;
        }
    }

    private async CRpcTask<EnterMatchFlowResult> EnterTableCoreAsync(
        AccountSession session,
        uint userId,
        EnterMatchStartInfo matchStart,
        int enterMatchTimeoutMs,
        int enterRoundTimeoutMs,
        IGameServerTransport? transport,
        EnterMatchFlowSessionState state)
    {
        _ = userId;
        try
        {
            await EnterMatchOnlyAsync(
                session,
                matchStart,
                TimeSpan.FromMilliseconds(enterMatchTimeoutMs),
                transport);
            var seatOrder = await EnterRoundOnlyAsync(
                session,
                matchStart,
                TimeSpan.FromMilliseconds(enterRoundTimeoutMs),
                transport,
                state);
            return CreateSuccessResult(session, matchStart, seatOrder, state);
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

    public static void InstallMatchProgressCapture(
        AccountSession session,
        EnterMatchFlowSessionState state)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(state);

        var previousPushHandler = session.PushMessageReceived;
        session.PushMessageReceived = message =>
        {
            state.CaptureFromAnyMessage(message);
            previousPushHandler?.Invoke(message);
        };
    }

    private static TimeoutException CreateMatchStartTimeoutException(
        AccountSession session,
        string? innerMessage = null)
    {
        return new TimeoutException(MatchStartDiagnostics.BuildMatchStartTimeoutMessage(session, innerMessage));
    }

    private static EnterMatchStartInfo? TryParseCapturedMatchStart(EnterMatchFlowSessionState state)
    {
        if (state.CapturedMatchStartMessage is not { } message)
        {
            return null;
        }

        return ParseMatchStartMessage(message);
    }

    private static bool IsEnterRoundProgressMessage(ProtocolMessage message)
    {
        return message.Kind switch
        {
            ProtocolMessageKind.EnterRoundAck => true,
            ProtocolMessageKind.InitGameTableAck => true,
            ProtocolMessageKind.AddGamePlayerInfoAck => true,
            ProtocolMessageKind.LordAck => true,
            _ => false,
        };
    }

    private static bool IsEnterRoundComplete(
        ProtocolMessage message,
        uint? resolvedSeat,
        EnterMatchFlowSessionState? state,
        uint userId)
    {
        _ = message;
        _ = state;
        _ = userId;
        return resolvedSeat is uint;
    }

    private static uint? ResolveSeatForUser(
        uint userId,
        EnterMatchFlowSessionState? state,
        EnterRoundAck? enterRoundAck)
    {
        if (state?.GetSeatForUser(userId) is uint seatFromPlayerInfo)
        {
            return seatFromPlayerInfo;
        }

        if (state?.InitGameTableAck is { } initGameTableAck)
        {
            for (var seatIndex = 0; seatIndex < initGameTableAck.Playerinfolist.Count; seatIndex++)
            {
                if (initGameTableAck.Playerinfolist[seatIndex].Userid == userId)
                {
                    return (uint)seatIndex;
                }
            }
        }

        if (enterRoundAck is not null && enterRoundAck.Userid == userId)
        {
            return enterRoundAck.Seatorder;
        }

        return null;
    }

    private static EnterMatchFlowResult Fail(
        AccountSession session,
        uint userId,
        uint matchId,
        byte[] ticketBytes,
        EnterMatchStartInfo matchStart,
        uint? tableId,
        InitGameTableAck? initGameTableAck,
        string failureMessage)
    {
        session.SetState(AccountSessionState.Failed);
        return new EnterMatchFlowResult
        {
            Success = false,
            UserId = userId,
            MatchId = matchId,
            Ticket = ticketBytes,
            TourneyId = matchStart.TourneyId,
            MatchPoint = matchStart.MatchPoint,
            GameId = matchStart.GameId,
            TableId = tableId,
            SeatUserMapping = BuildSeatUserMapping(initGameTableAck),
            FailureMessage = failureMessage,
        };
    }

    private static EnterMatchStartInfo ParseMatchStartMessage(ProtocolMessage message)
    {
        if (message.Kind == ProtocolMessageKind.StartGameClientAck)
        {
            var ack = message.StartGameClientAcknowledgement
                      ?? throw new InvalidOperationException("StartGameClientAck missing in server response.");
            return new EnterMatchStartInfo
            {
                MatchId = ack.Matchid,
                GameId = ack.Gameid,
                TourneyId = ack.Tourneyid,
                MatchPoint = ack.Matchpoint,
                Ticket = EncodeTicket(ack.Ticket),
            };
        }

        if (message.Kind == ProtocolMessageKind.StartClientExAck)
        {
            var ack = message.StartClientExAcknowledgement
                      ?? throw new InvalidOperationException("StartClientExAck missing in server response.");
            return new EnterMatchStartInfo
            {
                MatchId = ack.Matchid,
                GameId = ack.Gameid,
                TourneyId = ack.Tourneyid,
                MatchPoint = ack.Productid,
                Ticket = ack.Ticket?.ToArray() ?? Array.Empty<byte>(),
            };
        }

        throw new InvalidOperationException(
            $"Expected match start ack but received {message.Kind}.");
    }

    private static byte[] EncodeTicket(string ticket)
    {
        return string.IsNullOrEmpty(ticket)
            ? Array.Empty<byte>()
            : Encoding.UTF8.GetBytes(ticket);
    }

    private static uint? ResolveTableId(uint matchId, InitGameTableAck? initGameTableAck)
    {
        _ = initGameTableAck;
        return matchId;
    }

    private static IReadOnlyDictionary<uint, uint> BuildSeatUserMapping(InitGameTableAck? initGameTableAck)
    {
        if (initGameTableAck is null)
        {
            return new Dictionary<uint, uint>();
        }

        var mapping = new Dictionary<uint, uint>();
        for (var seatIndex = 0; seatIndex < initGameTableAck.Playerinfolist.Count; seatIndex++)
        {
            var player = initGameTableAck.Playerinfolist[seatIndex];
            if (player.Userid > 0)
            {
                mapping[(uint)seatIndex] = player.Userid;
            }
        }

        return mapping;
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

    private static void EnsureSignedUp(AccountSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureOnLoopThread(session);

        if (session.State is not (
            AccountSessionState.SignedUp
            or AccountSessionState.WaitingForMatch
            or AccountSessionState.EnteringMatch))
        {
            throw new InvalidOperationException(
                $"EnterMatchFlow requires session '{session.Alias}' to be signed up or entering match; current state is {session.State}.");
        }
    }

    private static void EnsureSignedUpUserId(AccountSession session, out uint userId)
    {
        EnsureSignedUp(session);
        if (session.UserId is not uint resolvedUserId || resolvedUserId == 0)
        {
            throw new InvalidOperationException(
                $"EnterMatchFlow requires session '{session.Alias}' to have a non-zero UserId.");
        }

        userId = resolvedUserId;
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
            throw new InvalidOperationException("EnterMatchFlow must run on the account session CRpcLoop thread.");
        }
    }
}