namespace LordUnion.IntegrationTests.Reporting;

public sealed class AccountGameEndSummary
{
    public string AccountAlias { get; init; } = string.Empty;

    public uint? GameFlowWinSeat { get; init; }

    public string? EndSignal { get; init; }
}