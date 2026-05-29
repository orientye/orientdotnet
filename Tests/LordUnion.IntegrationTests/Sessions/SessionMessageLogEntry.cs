using LordUnion.IntegrationTests.Protocol;

namespace LordUnion.IntegrationTests.Sessions;

public enum SessionMessageDirection
{
    Sent,
    Received,
}

public sealed class SessionMessageLogEntry
{
    public SessionMessageDirection Direction { get; init; }

    public string AccountAlias { get; init; } = string.Empty;

    public AccountSessionState State { get; init; }

    public ProtocolPhase Phase { get; init; }

    public ProtocolMessageKind Kind { get; init; }

    public uint Header0 { get; init; }

    public uint? Param { get; init; }

    public uint? UserId { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public string Description { get; init; } = string.Empty;
}