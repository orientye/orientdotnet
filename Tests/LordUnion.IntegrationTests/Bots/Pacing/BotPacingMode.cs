namespace LordUnion.IntegrationTests.Bots.Pacing;

public enum BotPacingMode
{
    Immediate,
    HumanRandom,
    /// <summary>Reserved: derive delay from server ack timestamps.</summary>
    ServerCountdown,
    /// <summary>Reserved: follow a recorded action timeline.</summary>
    ReplayTimeline,
}
