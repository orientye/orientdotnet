using LordUnion.IntegrationTests.Bots;
using LordUnion.IntegrationTests.Flows;
using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Protocol;

namespace LordUnion.IntegrationTests.Games.TKLord.Replay;

public sealed class XmlReplayBotPolicy : IBotPolicy
{
    private readonly XmlReplayCoordinator? coordinator;
    private readonly MinimalLandlordBotPolicy fallback;
    private readonly Dictionary<uint, int> bidIndexBySeat = new()
    {
        [0] = 0,
        [1] = 0,
        [2] = 0,
    };
    private readonly Dictionary<uint, int> playIndexBySeat = new()
    {
        [0] = 0,
        [1] = 0,
        [2] = 0,
    };
    private uint seat;
    private bool awaitingOwnPlayAck;
    private bool kickResponseSent;

    public BotGameState State => fallback.State;

    public static XmlReplayBotPolicy CreateReplay(
        XmlReplayScript script,
        uint seat,
        XmlReplayCoordinator? coordinator = null) =>
        new(seat, coordinator, script);

    public static XmlReplayBotPolicy CreateMinimalFallback(uint seat) =>
        new(seat, coordinator: null, script: null);

    public static XmlReplayBotPolicy CreateWithCoordinator(uint seat, XmlReplayCoordinator coordinator) =>
        new(seat, coordinator, script: null);

    private XmlReplayBotPolicy(uint seat, XmlReplayCoordinator? coordinator, XmlReplayScript? script)
    {
        this.coordinator = coordinator;
        ActiveScript = script;
        fallback = new MinimalLandlordBotPolicy();
        SetSeat(seat);
    }

    private XmlReplayScript? ActiveScript { get; set; }

    public void SetSeat(uint seat)
    {
        this.seat = seat;
        fallback.SetSeat(seat);
    }

    public void ApplyGameEvent(GameEvent gameEvent)
    {
        if (coordinator is not null && gameEvent.Kind == GameEventKind.CardsDealt)
        {
            coordinator.RegisterInitCard(seat, gameEvent.TestRecordId);
        }

        SyncActiveScriptFromCoordinator();

        if (ActiveScript is not null && gameEvent.Kind == GameEventKind.CardsDealt)
        {
            ResetHandReplayState();
        }

        if (ActiveScript is not null && IsOwnTakeoutAck(gameEvent))
        {
            SyncPlayIndexAfterServerAction();
        }

        fallback.ApplyGameEvent(gameEvent);

        if (gameEvent.Kind is GameEventKind.CardsPlayed or GameEventKind.PassPlayed
            && gameEvent.TakeoutMsgCnt is uint msgCnt
            && msgCnt > 0)
        {
            State.TakeoutMsgCnt = msgCnt;
        }

        if (ActiveScript is not null
            && State.LordSeat is uint lordSeat
            && gameEvent.Kind == GameEventKind.CardsPlayed
            && gameEvent.Seat == lordSeat
            && gameEvent.Cards is { Length: > 0 })
        {
            State.LandlordFirstLeadDone = true;
        }

    }

    public BotDecision? TryDecide(BotActionContext context)
    {
        SyncActiveScriptFromCoordinator();

        var script = ActiveScript;
        if (script is null)
        {
            return fallback.TryDecide(context);
        }

        var gameEvent = context.Event;
        return gameEvent.Kind switch
        {
            GameEventKind.ReadyRequested => BotDecision.Ready(),
            GameEventKind.CardsDealt when gameEvent.FirstCallSeat == seat => DecideBid(context, script),
            GameEventKind.BidRequested when gameEvent.NextCallSeat == seat => DecideBid(context, script),
            GameEventKind.TurnStarted
                when gameEvent.OperateTypes?.Contains(1u) == true
                    && gameEvent.SeatList?.Contains(seat) == true
                    && !IsLordSeat()
                    && !kickResponseSent =>
                DecideDeclineKick(),
            // Only the landlord opens the takeout phase (one first lead after kick OperateFinished).
            GameEventKind.OperateFinished
                when IsLordSeat()
                    && !State.LandlordFirstLeadDone
                    && !awaitingOwnPlayAck
                    && gameEvent.OperateTypes?.Contains(1u) == true =>
                DecideLordFirstLead(context, script),
            GameEventKind.CardsPlayed or GameEventKind.PassPlayed
                when gameEvent.NextPlayer == seat
                    && !IsNextPlayerServerAuto(gameEvent)
                    && CanRespondToTakeoutTurn()
                    && CanFollowTakeoutAck(gameEvent) =>
                DecidePlayWhenMyTurn(context, script),
            _ => null,
        };
    }

    private BotDecision DecideDeclineKick()
    {
        kickResponseSent = true;
        GameFlowTrace.LogSendDecision(
            $"seat{seat}",
            userId: null,
            reqSeat: seat,
            "Kick",
            "OperateStart",
            State.LordSeat,
            playIndex: null,
            xmlCards: "no",
            encodedCount: null);
        return BotDecision.DeclineKick();
    }

    private BotDecision DecideBid(BotActionContext context, XmlReplayScript script)
    {
        var bids = script.BidsBySeat[seat];
        var bidIndex = bidIndexBySeat[seat];
        if (bidIndex >= bids.Count)
        {
            throw new InvalidOperationException(
                $"XML replay exhausted bid actions for seat {seat} in {script.TestRecordId}.");
        }

        bidIndexBySeat[seat] = bidIndex + 1;
        var recorded = bids[bidIndex];
        var evt = context.Event;
        // Req curcallseat is this player's seat; ack Curcallseat is the previous bidder.
        var nextCallSeat = evt.Kind == GameEventKind.CardsDealt
            ? evt.FirstCallSeat ?? seat
            : evt.NextCallSeat ?? seat;
        return BotDecision.Bid(
            seat,
            nextCallSeat,
            evt.ValidateScore ?? 0,
            recorded.BidScore);
    }

    private BotDecision? DecidePlayWhenMyTurn(BotActionContext context, XmlReplayScript script) =>
        DecidePlay(context, script, "TakeoutNextPlayer");

    private BotDecision? DecideLordFirstLead(BotActionContext context, XmlReplayScript script) =>
        DecidePlay(context, script, "KickFinished");

    private BotDecision? DecidePlay(BotActionContext context, XmlReplayScript script, string trigger = "PlayTurn")
    {
        if (awaitingOwnPlayAck)
        {
            LogReplayBlocked(trigger, "awaitingOwnPlayAck");
            return null;
        }

        var plays = script.PlaysBySeat[seat];
        var playIndex = playIndexBySeat[seat];
        if (playIndex >= plays.Count)
        {
            throw new InvalidOperationException(
                $"XML replay exhausted play actions for seat {seat} in {script.TestRecordId}.");
        }

        awaitingOwnPlayAck = true;
        var recorded = plays[playIndex];
        var evt = context.Event;
        var nextPlayer = evt.NextPlayer ?? NextSeatAfter(seat);
        var passPlayer = evt.PassPlayer ?? (int)seat;

        if (recorded.IsPass)
        {
            LogReplayDecision(trigger, playIndex, recorded.CardString, null, BotDecisionKind.Pass);
            return BotDecision.Pass(nextPlayer, passPlayer);
        }

        var encoded = XmlCardCodec.DecodePlayString(recorded.CardString);
        LogReplayDecision(trigger, playIndex, recorded.CardString, encoded.Length, BotDecisionKind.Play);
        return BotDecision.Play(nextPlayer, encoded);
    }

    private void LogReplayDecision(
        string trigger,
        int playIndex,
        string? xmlCards,
        int? encodedCount,
        BotDecisionKind kind)
    {
        GameFlowTrace.LogSendDecision(
            $"seat{seat}",
            userId: null,
            reqSeat: seat,
            kind.ToString(),
            trigger,
            State.LordSeat,
            playIndex,
            xmlCards,
            encodedCount,
            State.TakeoutMsgCnt > 0 ? State.TakeoutMsgCnt : null);
    }

    private static uint NextSeatAfter(uint currentSeat) => (currentSeat + 1) % 3;

    private bool IsLordSeat() => State.LordSeat == seat;

    private static bool IsNextPlayerServerAuto(GameEvent gameEvent) =>
        gameEvent.NextAutoPass == true || gameEvent.NextAutoGo == true;

    /// <summary>
    /// Farmers must wait until the landlord's first lead is observed on the wire.
    /// The landlord also waits here until first lead is done (opens only via OperateFinished).
    /// When <see cref="BotGameState.LordSeat"/> is unset (unit tests), preserve legacy behaviour.
    /// </summary>
    private bool CanRespondToTakeoutTurn() =>
        State.LordSeat is null
        || State.LandlordFirstLeadDone;

    /// <summary>
    /// Gates when a farmer may act on a takeout ack. Before the landlord opening is observed
    /// (<see cref="BotGameState.LandlordFirstLeadDone"/>), only the landlord's
    /// <see cref="GameEventKind.CardsPlayed"/> unlocks the first XML play (playIndex 0).
    /// After opening lead, playIndex 0 may also trigger on upstream takeout acks when it is
    /// this seat's turn (<see cref="TryDecide"/> already requires <c>NextPlayer == seat</c>).
    /// </summary>
    private bool CanFollowTakeoutAck(GameEvent gameEvent)
    {
        if (IsLordSeat() || State.LordSeat is null)
        {
            return true;
        }

        if (playIndexBySeat[seat] > 0)
        {
            return true;
        }

        if (State.LandlordFirstLeadDone)
        {
            return gameEvent.Kind is GameEventKind.CardsPlayed or GameEventKind.PassPlayed;
        }

        return gameEvent.Kind == GameEventKind.CardsPlayed
            && gameEvent.Seat == State.LordSeat
            && gameEvent.Cards is { Length: > 0 };
    }

    private void LogReplayBlocked(string trigger, string reason)
    {
        GameFlowTrace.LogSendDecision(
            $"seat{seat}",
            userId: null,
            reqSeat: seat,
            "blocked",
            $"{trigger}:{reason}",
            State.LordSeat,
            playIndexBySeat[seat],
            xmlCards: null,
            encodedCount: null,
            State.TakeoutMsgCnt > 0 ? State.TakeoutMsgCnt : null);
    }

    private void ResetHandReplayState()
    {
        bidIndexBySeat[0] = 0;
        bidIndexBySeat[1] = 0;
        bidIndexBySeat[2] = 0;
        playIndexBySeat[0] = 0;
        playIndexBySeat[1] = 0;
        playIndexBySeat[2] = 0;
        awaitingOwnPlayAck = false;
        kickResponseSent = false;
        State.TakeoutMsgCnt = 0;
        State.LandlordFirstLeadDone = false;
    }

    private void SyncActiveScriptFromCoordinator()
    {
        if (coordinator is null || !coordinator.IsReplayActive)
        {
            return;
        }

        ActiveScript = coordinator.Catalog?.Script;
    }

    /// <summary>
    /// Play ack <see cref="GameEvent.Seat"/> is the player who played cards.
    /// Pass ack <see cref="GameEvent.Seat"/> is the last non-pass player; <see cref="GameEvent.PassPlayer"/> is who passed.
    /// </summary>
    private bool IsOwnTakeoutAck(GameEvent gameEvent) =>
        gameEvent.Kind switch
        {
            GameEventKind.CardsPlayed => gameEvent.Seat == seat,
            GameEventKind.PassPlayed => gameEvent.PassPlayer == (int)seat,
            _ => false,
        };

    private void SyncPlayIndexAfterServerAction()
    {
        awaitingOwnPlayAck = false;

        var plays = ActiveScript!.PlaysBySeat[seat];
        var playIndex = playIndexBySeat[seat];
        if (playIndex < plays.Count)
        {
            playIndexBySeat[seat] = playIndex + 1;
        }
    }
}
