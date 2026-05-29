using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Sessions;

namespace LordUnion.IntegrationTests.Bots.Pacing;

/// <summary>
/// Context passed to <see cref="IActionScheduler"/> before sending a bot request.
/// Carries the source ack for future server-countdown and replay schedulers.
/// </summary>
public sealed record BotSendContext(
    AccountSession Session,
    GameVariants.GameEvent Event,
    ProtocolMessage SourceMessage,
    BotDecision Decision,
    DateTimeOffset ReceivedAt);