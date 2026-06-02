using CRpc.Async;
using LordUnion.IntegrationTests.Bots;
using LordUnion.IntegrationTests.Bots.Pacing;
using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Sessions;


namespace LordUnion.IntegrationTests.Flows;

/// <summary>
/// Drives one account through in-game lord messages until <see cref="GameEventKind.GameFinished"/>.
/// Decision via <see cref="IBotPolicy"/>; pacing via <see cref="IActionScheduler"/>.
/// </summary>
internal sealed class GameFlow

{
    private readonly ServerProtocolCodec codec;


    public GameFlow(ServerProtocolCodec? codec = null)

    {
        this.codec = codec ?? new ServerProtocolCodec();
    }


    public CRpcTask<GameFlowResult> RunUntilFinishedAsync(
        AccountSession session,
        MinimalLandlordBot bot,
        ILordGameVariant variant,
        TimeSpan gameOverTimeout,
        IGameServerTransport? transport = null,
        TableGamePhaseCoordinator? tableGamePhase = null)

    {
        return RunUntilFinishedAsync(
            session,
            new MinimalLandlordBotPolicy(bot),
            variant,
            gameOverTimeout,
            ImmediateActionScheduler.Instance,
            transport,
            tableGamePhase);
    }


    public CRpcTask<GameFlowResult> RunUntilFinishedAsync(
        AccountSession session,
        IBotPolicy policy,
        ILordGameVariant variant,
        TimeSpan gameOverTimeout,
        IActionScheduler scheduler,
        IGameServerTransport? transport = null,
        TableGamePhaseCoordinator? tableGamePhase = null)

    {
        return RunCoreAsync(session, policy, variant, gameOverTimeout, scheduler, transport, tableGamePhase);
    }


    private async CRpcTask<GameFlowResult> RunCoreAsync(
        AccountSession session,
        IBotPolicy policy,
        ILordGameVariant variant,
        TimeSpan gameOverTimeout,
        IActionScheduler scheduler,
        IGameServerTransport? transport,
        TableGamePhaseCoordinator? tableGamePhase)

    {
        ArgumentNullException.ThrowIfNull(session);

        ArgumentNullException.ThrowIfNull(policy);

        ArgumentNullException.ThrowIfNull(variant);

        ArgumentNullException.ThrowIfNull(scheduler);

        EnsureOnLoopThread(session);


        if (session.State is not (AccountSessionState.InGame or AccountSessionState.Finished))

        {
            throw new InvalidOperationException(
                $"GameFlow requires session '{session.Alias}' to be in game; current state is {session.State}.");
        }


        if (session.SeatOrder is not uint seatOrder)

        {
            throw new InvalidOperationException(
                $"GameFlow requires session '{session.Alias}' to have a resolved SeatOrder.");
        }


        policy.SetSeat(seatOrder);

        GameFlowTrace.LogGameFlowStart(
            session.Alias,
            session.UserId,
            seatOrder,
            session.MatchId);

        transport?.BindIncomingHandler(session, codec);


        var completionSource = new CRpcTaskCompletionSource<GameFlowResult>(session.Loop);

        var timeoutMs = ToTimeoutMilliseconds(gameOverTimeout);

        var previousPushHandler = session.PushMessageReceived;


        session.PushMessageReceived = message =>

        {
            previousPushHandler?.Invoke(message);

            HandlePushMessage(
                session,
                policy,
                variant,
                scheduler,
                transport,
                tableGamePhase,
                message,
                completionSource);
        };


        try

        {
            if (timeoutMs >= 0 || tableGamePhase is not null)

            {
                _ = ArmOverallTimeoutAsync(session, timeoutMs, tableGamePhase, completionSource);
            }


            var result = await completionSource.Task;

            session.SetState(result.Success ? AccountSessionState.Finished : AccountSessionState.Failed);

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

        finally

        {
            session.PushMessageReceived = previousPushHandler;
        }
    }


    private void HandlePushMessage(
        AccountSession session,
        IBotPolicy policy,
        ILordGameVariant variant,
        IActionScheduler scheduler,
        IGameServerTransport? transport,
        TableGamePhaseCoordinator? tableGamePhase,
        ProtocolMessage message,
        CRpcTaskCompletionSource<GameFlowResult> completionSource)

    {
        if (TryCompleteOnTableGrace(session, tableGamePhase, completionSource))
        {
            return;
        }

        if (TryCompleteOnGameEnd(session, tableGamePhase, message, completionSource))

        {
            return;
        }


        var ack = message.Acknowledgement;

        if (ack is null || message.LordAcknowledgement is null || !variant.CanHandle(ack))

        {
            return;
        }


        var gameEvent = variant.DecodeGameEvent(ack);

        if (gameEvent is null)

        {
            return;
        }


        policy.ApplyGameEvent(gameEvent);

        if (gameEvent.Kind == GameEventKind.CardsDealt)
        {
            GameFlowTrace.LogCardsDealt(
                session.Alias,
                session.UserId,
                policy.State.MySeat,
                gameEvent.TestRecordId,
                gameEvent.FirstCallSeat);
        }

        if (gameEvent.Kind == GameEventKind.LandlordDeclared)
        {
            GameFlowTrace.LogSendDecision(
                session.Alias,
                session.UserId,
                policy.State.MySeat,
                "LandlordDeclared",
                "ack",
                gameEvent.LordSeat,
                playIndex: null,
                xmlCards: null,
                encodedCount: null);
        }

        if (gameEvent.Kind == GameEventKind.TurnStarted)
        {
            GameFlowTrace.LogOperateStart(
                session.Alias,
                session.UserId,
                policy.State.MySeat,
                gameEvent.OperateTypes,
                gameEvent.SeatList);
        }

        if (gameEvent.Kind == GameEventKind.KickAck)
        {
            GameFlowTrace.LogKickAck(
                session.Alias,
                session.UserId,
                gameEvent.Seat ?? policy.State.MySeat,
                gameEvent.Kick ?? false);
        }

        if (gameEvent.Kind is GameEventKind.CardsPlayed or GameEventKind.PassPlayed)
        {
            GameFlowTrace.LogTakeoutAck(
                session.Alias,
                session.UserId,
                policy.State.MySeat,
                gameEvent.Kind,
                gameEvent.Seat,
                gameEvent.NextPlayer,
                gameEvent.TakeoutMsgCnt,
                gameEvent.Cards?.Length ?? 0,
                gameEvent.NextAutoPass,
                gameEvent.NextAutoGo,
                gameEvent.PassPlayer);
        }

        if (gameEvent.Kind == GameEventKind.GameFinished)

        {
            tableGamePhase?.NotifyFirstGameEnded();
            completionSource.TrySetResult(new GameFlowResult
            {
                Success = true,
                WinSeat = gameEvent.WinSeat,
                EndSignal = "GameFinished",
                Scores = gameEvent.Scores,
            });
            GameFlowTrace.LogGameEnd(session.Alias, "GameFinished", gameEvent.WinSeat);

            return;
        }


        _ = RespondToGameEventAsync(session, policy, variant, scheduler, transport, message, gameEvent);
    }


    private static bool TryCompleteOnTableGrace(
        AccountSession session,
        TableGamePhaseCoordinator? tableGamePhase,
        CRpcTaskCompletionSource<GameFlowResult> completionSource)
    {
        if (tableGamePhase is null || !tableGamePhase.IsGraceExpired)
        {
            return false;
        }

        if (completionSource.TrySetResult(CreateTableGraceExpiredResult()))
        {
            GameFlowTrace.LogGameEnd(session.Alias, "TableGracePeriod", winSeat: null);
            return true;
        }

        return completionSource.Task.GetAwaiter().IsCompleted;
    }

    private static bool TryCompleteOnGameEnd(
        AccountSession session,
        TableGamePhaseCoordinator? tableGamePhase,
        ProtocolMessage message,
        CRpcTaskCompletionSource<GameFlowResult> completionSource)

    {
        if (message.LordAcknowledgement?.LordresultAckMsg is { } resultAck)
        {
            tableGamePhase?.NotifyFirstGameEnded();
            completionSource.TrySetResult(new GameFlowResult
            {
                Success = true,
                WinSeat = resultAck.Winseat,
                EndSignal = "LordResultAck",
                Scores = resultAck.Score.ToList(),
            });
            GameFlowTrace.LogGameEnd(session.Alias, "LordResultAck", resultAck.Winseat);
            return true;
        }

        if (message.OverGameAcknowledgement is not null
            || message.Acknowledgement?.MatchAckMsg?.OvergameAckMsg is not null)
        {
            tableGamePhase?.NotifyFirstGameEnded();
            completionSource.TrySetResult(new GameFlowResult
            {
                Success = true,
                EndSignal = "OverGameAck",
            });
            GameFlowTrace.LogGameEnd(session.Alias, "OverGameAck", winSeat: null);
            return true;
        }

        if (message.HandOverAcknowledgement is not null
            || message.Acknowledgement?.MatchAckMsg?.HandoverAckMsg is not null)
        {
            tableGamePhase?.NotifyFirstGameEnded();
            completionSource.TrySetResult(new GameFlowResult
            {
                Success = true,
                EndSignal = "HandOverAck",
            });
            GameFlowTrace.LogGameEnd(session.Alias, "HandOverAck", winSeat: null);
            return true;
        }

        return false;
    }


    private async CRpcTask RespondToGameEventAsync(
        AccountSession session,
        IBotPolicy policy,
        ILordGameVariant variant,
        IActionScheduler scheduler,
        IGameServerTransport? transport,
        ProtocolMessage sourceMessage,
        GameEvent gameEvent)

    {
        EnsureOnLoopThread(session);


        var matchId = session.MatchId ?? gameEvent.MatchId;

        var seat = policy.State.MySeat;

        var decision = policy.TryDecide(new BotActionContext(gameEvent, sourceMessage, matchId, seat));

        if (decision is null)

        {
            return;
        }

        LogOutgoingDecision(
            session,
            seat,
            gameEvent.Kind,
            decision,
            policy.State.LordSeat,
            policy.State.TakeoutMsgCnt);

        var sendContext = new BotSendContext(
            session,
            gameEvent,
            sourceMessage,
            decision,
            DateTimeOffset.UtcNow);


        await scheduler.WaitBeforeSendAsync(sendContext, session.Loop);


        var request = BuildRequest(variant, decision, matchId, seat, policy.State.TakeoutMsgCnt);

        await SendRequestAsync(session, transport, request);
    }


    private static TKMobileReqMsg BuildRequest(
        ILordGameVariant variant,
        BotDecision decision,
        uint matchId,
        uint seat,
        uint takeoutMsgCnt)

    {
        return decision.Kind switch

        {
            BotDecisionKind.Ready => variant.BuildReadyReq(matchId, seat),

            BotDecisionKind.Bid => variant.BuildBidReq(
                matchId,
                decision.CurCallSeat ?? seat,
                decision.NextCallSeat ?? seat,
                decision.CurScore ?? 0,
                decision.ValidateScore ?? 0),

            BotDecisionKind.Play => variant.BuildPlayCardsReq(
                matchId,
                seat,
                decision.NextPlayer ?? seat,
                decision.Cards ?? Array.Empty<byte>(),
                msgCnt: takeoutMsgCnt),

            BotDecisionKind.Pass => variant.BuildPassReq(
                matchId,
                seat,
                decision.NextPlayer ?? seat,
                (uint)(decision.PassPlayer ?? (int)seat),
                msgCnt: takeoutMsgCnt),

            BotDecisionKind.Kick => variant.BuildKickReq(matchId, seat, decision.Kick ?? false),

            _ => throw new InvalidOperationException($"Unsupported decision kind: {decision.Kind}"),
        };
    }


    private async CRpcTask ArmOverallTimeoutAsync(
        AccountSession session,
        int timeoutMs,
        TableGamePhaseCoordinator? tableGamePhase,
        CRpcTaskCompletionSource<GameFlowResult> completionSource)

    {
        var loop = session.Loop;
        var deadlineUtc = timeoutMs < 0
            ? DateTimeOffset.MaxValue
            : DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);

        while (!completionSource.Task.GetAwaiter().IsCompleted)
        {
            if (TryCompleteOnTableGrace(session, tableGamePhase, completionSource))
            {
                return;
            }

            if (DateTimeOffset.UtcNow >= deadlineUtc)
            {
                completionSource.TrySetException(new TimeoutException(
                    $"Timed out waiting for game end (LordResultAck, OverGameAck, HandOverAck, GameFinished, or table grace) on account '{session.Alias}' in state {session.State} (phase {session.CurrentPhase})."));
                return;
            }

            await CRpcTask.Delay(250, loop);
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


    private static GameFlowResult CreateTableGraceExpiredResult() =>
        new()
        {
            Success = true,
            EndSignal = "TableGracePeriod",
            FailureMessage = "Table grace period expired before this account received a game end signal.",
        };

    private static int ToTimeoutMilliseconds(TimeSpan timeout)

    {
        if (timeout <= TimeSpan.Zero)

        {
            return 0;
        }


        return (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue);
    }


    private static void LogOutgoingDecision(
        AccountSession session,
        uint reqSeat,
        GameEventKind trigger,
        BotDecision decision,
        uint? lordSeat,
        uint takeoutMsgCnt)
    {
        var xmlCards = decision.Kind is BotDecisionKind.Play or BotDecisionKind.Pass
            ? FormatCardBytes(decision.Cards)
            : null;
        GameFlowTrace.LogSendDecision(
            session.Alias,
            session.UserId,
            reqSeat,
            decision.Kind.ToString(),
            trigger.ToString(),
            lordSeat,
            playIndex: null,
            xmlCards,
            decision.Cards?.Length,
            takeoutMsgCnt > 0 ? takeoutMsgCnt : null);
    }

    private static string FormatCardBytes(byte[]? cards) =>
        cards is null || cards.Length == 0
            ? "<pass>"
            : string.Join(",", cards);

    private static void EnsureOnLoopThread(AccountSession session)

    {
        if (!session.Loop.IsInLoopThread)

        {
            throw new InvalidOperationException("GameFlow must run on the account session CRpcLoop thread.");
        }
    }
}