using LordUnion.IntegrationTests.Flows;

namespace LordUnion.IntegrationTests.Reporting;

public sealed class AccountCleanupSummary
{
    public string AccountAlias { get; init; } = string.Empty;

    public bool Completed { get; init; }

    public bool UnsignupSent { get; init; }

    public bool UnsignupAckReceived { get; init; }

    public uint? UnsignupParam { get; init; }

    public IReadOnlyList<uint> DiscoveredMatchIds { get; init; } = Array.Empty<uint>();

    public IReadOnlyList<uint> ExitGameAttemptedMatchIds { get; init; } = Array.Empty<uint>();

    public IReadOnlyList<uint> ExitMatchAttemptedMatchIds { get; init; } = Array.Empty<uint>();

    public string? ErrorMessage { get; init; }

    public static AccountCleanupSummary FromResult(
        string accountAlias,
        AccountCleanupFlowResult? result,
        string? errorMessage)
    {
        return new AccountCleanupSummary
        {
            AccountAlias = accountAlias,
            Completed = result is not null && errorMessage is null,
            UnsignupSent = result?.UnsignupSent ?? false,
            UnsignupAckReceived = result?.UnsignupAckReceived ?? false,
            UnsignupParam = result?.UnsignupParam,
            DiscoveredMatchIds = result?.DiscoveredMatchIds ?? Array.Empty<uint>(),
            ExitGameAttemptedMatchIds = result?.ExitGameAttemptedMatchIds ?? Array.Empty<uint>(),
            ExitMatchAttemptedMatchIds = result?.ExitMatchAttemptedMatchIds ?? Array.Empty<uint>(),
            ErrorMessage = errorMessage,
        };
    }
}
