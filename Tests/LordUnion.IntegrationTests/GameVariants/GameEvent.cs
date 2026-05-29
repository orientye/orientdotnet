namespace LordUnion.IntegrationTests.GameVariants;

public enum GameEventKind
{
    ReadyRequested,
    GameStarted,
    CardsDealt,
    BidRequested,
    LandlordDeclared,
    TurnStarted,
    CardsPlayed,
    PassPlayed,
    GameFinished,
}

/// <summary>
/// Variant-neutral game event decoded from server acks. Only fields relevant to
/// <see cref="Kind"/> are populated; others remain null.
/// </summary>
public sealed record GameEvent
{
    public required GameEventKind Kind { get; init; }

    public uint MatchId { get; init; }

    public uint? Seat { get; init; }

    public uint? NextPlayer { get; init; }

    public int? PassPlayer { get; init; }

    public byte[]? Cards { get; init; }

    public ulong? Timestamp { get; init; }

    public uint? CurCallSeat { get; init; }

    public uint? NextCallSeat { get; init; }

    public uint? CurScore { get; init; }

    public uint? ValidateScore { get; init; }

    public uint? FirstCallSeat { get; init; }

    public uint? LordSeat { get; init; }

    public IReadOnlyList<uint>? OperateTypes { get; init; }

    public IReadOnlyList<uint>? SeatList { get; init; }

    public uint? WinSeat { get; init; }

    public IReadOnlyList<int>? Scores { get; init; }
}