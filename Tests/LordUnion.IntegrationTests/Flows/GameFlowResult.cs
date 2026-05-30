namespace LordUnion.IntegrationTests.Flows;

internal sealed class GameFlowResult
{
    public bool Success { get; init; }

    public uint? WinSeat { get; init; }

    /// <summary>How the game loop ended (e.g. LordResultAck, OverGameAck, GameFinished).</summary>
    public string? EndSignal { get; init; }

    public IReadOnlyList<int>? Scores { get; init; }

    public string? FailureMessage { get; init; }
}