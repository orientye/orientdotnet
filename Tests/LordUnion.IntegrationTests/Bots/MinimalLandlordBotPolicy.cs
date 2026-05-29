using LordUnion.IntegrationTests.GameVariants;

namespace LordUnion.IntegrationTests.Bots;

/// <summary>
/// Classic minimal heuristic policy backed by <see cref="MinimalLandlordBot"/>.
/// </summary>
public sealed class MinimalLandlordBotPolicy : IBotPolicy
{
    private readonly MinimalLandlordBot bot;

    public MinimalLandlordBotPolicy(MinimalLandlordBot? bot = null)
    {
        this.bot = bot ?? new MinimalLandlordBot();
    }

    public BotGameState State => bot.State;

    public void SetSeat(uint seat) => bot.SetSeat(seat);

    public void ApplyGameEvent(GameEvent gameEvent) => bot.ApplyGameEvent(gameEvent);

    public BotDecision? TryDecide(BotActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var gameEvent = context.Event;
        var seat = context.Seat;
        var matchId = context.MatchId;

        return gameEvent.Kind switch
        {
            GameEventKind.ReadyRequested => bot.DecideReady(),
            GameEventKind.CardsDealt when gameEvent.FirstCallSeat == seat => bot.DecideBid(
                new BidContext(
                    matchId,
                    seat,
                    gameEvent.FirstCallSeat ?? seat,
                    gameEvent.CurScore ?? 0,
                    gameEvent.ValidateScore ?? 0)),
            GameEventKind.BidRequested
                when gameEvent.NextCallSeat == seat && gameEvent.NextCallSeat != uint.MaxValue => bot.DecideBid(
                    new BidContext(
                        matchId,
                        seat,
                        gameEvent.NextCallSeat ?? seat,
                        gameEvent.CurScore ?? 0,
                        gameEvent.ValidateScore ?? 0)),
            GameEventKind.LandlordDeclared
                when gameEvent.LordSeat == seat && !bot.State.LandlordFirstLeadDone => DecideFirstLead(
                    bot,
                    matchId,
                    seat),
            GameEventKind.TurnStarted when IsLeadTurn(gameEvent, seat) && !bot.State.LandlordFirstLeadDone =>
                DecideFirstLead(
                    bot,
                    matchId,
                    seat),
            GameEventKind.CardsPlayed or GameEventKind.PassPlayed when gameEvent.NextPlayer == seat => bot.DecidePlay(
                new PlayContext(
                    matchId,
                    seat,
                    gameEvent.NextPlayer ?? seat,
                    gameEvent.PassPlayer ?? 0,
                    gameEvent.Cards)),
            _ => null,
        };
    }

    private static bool IsLeadTurn(GameEvent gameEvent, uint seat)
    {
        return gameEvent.SeatList is { Count: > 0 } seatList && seatList[0] == seat;
    }

    private static BotDecision DecideFirstLead(MinimalLandlordBot bot, uint matchId, uint seat)
    {
        bot.State.LandlordFirstLeadDone = true;
        return bot.DecidePlay(new PlayContext(matchId, seat, seat, 0));
    }
}