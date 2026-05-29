using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Protocol;

namespace LordUnion.IntegrationTests.Bots;

/// <summary>
/// Decides in-game actions (ready, bid, play, pass). Timing is handled by <see cref="Pacing.IActionScheduler"/>.
/// </summary>
public interface IBotPolicy
{
    BotGameState State { get; }

    void SetSeat(uint seat);

    void ApplyGameEvent(GameEvent gameEvent);

    /// <summary>Returns null when the event does not require a response from this seat.</summary>
    BotDecision? TryDecide(BotActionContext context);
}

public sealed record BotActionContext(
    GameEvent Event,
    ProtocolMessage SourceMessage,
    uint MatchId,
    uint Seat);