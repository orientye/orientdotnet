namespace LordUnion.IntegrationTests.Reporting;

public sealed class AccountPhaseTiming
{
    public string AccountAlias { get; init; } = string.Empty;

    public TimeSpan ConnectDuration { get; init; }

    public TimeSpan LoginDuration { get; init; }

    public TimeSpan SignupDuration { get; init; }

    public TimeSpan EnterMatchDuration { get; init; }

    public TimeSpan GameDuration { get; init; }

    public TimeSpan TotalDuration =>
        ConnectDuration + LoginDuration + SignupDuration + EnterMatchDuration + GameDuration;
}

public sealed class ScenarioReport
{
    public bool Success { get; init; }

    public IReadOnlyList<AccountPhaseTiming> AccountTimings { get; init; } =
        Array.Empty<AccountPhaseTiming>();

    public ScenarioFailureDetail? FirstFailure { get; init; }

    public uint? MatchId { get; init; }

    public uint? TableId { get; init; }

    public IReadOnlyDictionary<uint, uint>? SeatUserMapping { get; init; }

    public uint? WinSeat { get; init; }

    public IReadOnlyList<SignupDiagnosticSnapshot> SignupDiagnostics { get; init; } =
        Array.Empty<SignupDiagnosticSnapshot>();
}
