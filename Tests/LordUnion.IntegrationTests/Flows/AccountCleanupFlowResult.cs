namespace LordUnion.IntegrationTests.Flows;

public sealed class AccountCleanupFlowResult
{
    public bool UnsignupSent { get; init; }

    public bool UnsignupAckReceived { get; init; }

    public uint? UnsignupParam { get; init; }

    public IReadOnlyList<uint> DiscoveredMatchIds { get; init; } = Array.Empty<uint>();

    public IReadOnlyList<uint> ExitGameAttemptedMatchIds { get; init; } = Array.Empty<uint>();

    public IReadOnlyList<uint> ExitMatchAttemptedMatchIds { get; init; } = Array.Empty<uint>();
}
