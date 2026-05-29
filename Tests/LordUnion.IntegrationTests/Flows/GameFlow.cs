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
public sealed class GameFlow

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
        IGameServerTransport? transport = null)

    {
        return RunUntilFinishedAsync(
            session,
            new MinimalLandlordBotPolicy(bot),
            variant,
            gameOverTimeout,
            ImmediateActionScheduler.Instance,
            transport);
    }


    public CRpcTask<GameFlowResult> RunUntilFinishedAsync(
        AccountSession session,
        IBotPolicy policy,
        ILordGameVariant variant,
        TimeSpan gameOverTimeout,
        IActionScheduler scheduler,
        IGameServerTransport? transport = null)

    {
        return RunCoreAsync(session, policy, variant, gameOverTimeout, scheduler, transport);
    }


    private async CRpcTask<GameFlowResult> RunCoreAsync(
        AccountSession session,
        IBotPolicy policy,
        ILordGameVariant variant,
        TimeSpan gameOverTimeout,
        IActionScheduler scheduler,
        IGameServerTransport? transport)

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

        transport?.BindIncomingHandler(session, codec);


        var completionSource = new CRpcTaskCompletionSource<GameFlowResult>(session.Loop);

        var timeoutMs = ToTimeoutMilliseconds(gameOverTimeout);

        var previousPushHandler = session.PushMessageReceived;


        session.PushMessageReceived = message =>

        {
            previousPushHandler?.Invoke(message);

            HandlePushMessage(session, policy, variant, scheduler, transport, message, completionSource);
        };


        try

        {
            if (timeoutMs >= 0)

            {
                _ = ArmOverallTimeoutAsync(session, timeoutMs, completionSource);
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
        ProtocolMessage message,
        CRpcTaskCompletionSource<GameFlowResult> completionSource)

    {
        if (TryCompleteOnMatchOver(message, completionSource))

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


        if (gameEvent.Kind == GameEventKind.GameFinished)

        {
            completionSource.TrySetResult(new GameFlowResult

            {
                Success = true,

                WinSeat = gameEvent.WinSeat,

                EndSignal = "GameFinished",

                Scores = gameEvent.Scores,
            });

            return;
        }


        _ = RespondToGameEventAsync(session, policy, variant, scheduler, transport, message, gameEvent);
    }


    private static bool TryCompleteOnMatchOver(
        ProtocolMessage message,
        CRpcTaskCompletionSource<GameFlowResult> completionSource)

    {
        if (message.LordAcknowledgement?.LordresultAckMsg is not { } resultAck)

        {
            return false;
        }


        completionSource.TrySetResult(new GameFlowResult

        {
            Success = true,

            WinSeat = resultAck.Winseat,

            EndSignal = "LordResultAck",

            Scores = resultAck.Score.ToList(),
        });

        return true;
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


        var sendContext = new BotSendContext(
            session,
            gameEvent,
            sourceMessage,
            decision,
            DateTimeOffset.UtcNow);


        await scheduler.WaitBeforeSendAsync(sendContext, session.Loop);


        var request = BuildRequest(variant, decision, matchId, seat);

        await SendRequestAsync(session, transport, request);
    }


    private static TKMobileReqMsg BuildRequest(
        ILordGameVariant variant,
        BotDecision decision,
        uint matchId,
        uint seat)

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
                decision.Cards ?? Array.Empty<byte>()),

            BotDecisionKind.Pass => variant.BuildPassReq(
                matchId,
                seat,
                decision.NextPlayer ?? seat,
                (uint)(decision.PassPlayer ?? (int)seat)),

            _ => throw new InvalidOperationException($"Unsupported decision kind: {decision.Kind}"),
        };
    }


    private async CRpcTask ArmOverallTimeoutAsync(
        AccountSession session,
        int timeoutMs,
        CRpcTaskCompletionSource<GameFlowResult> completionSource)

    {
        await CRpcTask.Delay(timeoutMs, session.Loop);


        if (completionSource.Task.GetAwaiter().IsCompleted)

        {
            return;
        }


        completionSource.TrySetException(new TimeoutException(
            $"Timed out waiting for GameFinished on account '{session.Alias}' in state {session.State} (phase {session.CurrentPhase})."));
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
            throw new InvalidOperationException("GameFlow must run on the account session CRpcLoop thread.");
        }
    }
}