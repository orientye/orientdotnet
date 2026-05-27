namespace LordUnion.IntegrationTests.Flows;

public sealed class GameFlowResult
{
    public bool Success { get; init; }

    public uint? WinSeat { get; init; }

    public IReadOnlyList<int>? Scores { get; init; }

    public string? FailureMessage { get; init; }
}
