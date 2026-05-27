namespace LordUnion.IntegrationTests.Flows;

public sealed class EnterMatchFlowResult
{
    public bool Success { get; init; }

    public uint? UserId { get; init; }

    public uint MatchId { get; init; }

    public byte[] Ticket { get; init; } = Array.Empty<byte>();

    public uint TourneyId { get; init; }

    public uint MatchPoint { get; init; }

    public uint GameId { get; init; }

    public uint? TableId { get; init; }

    public uint SeatOrder { get; init; }

    public IReadOnlyDictionary<uint, uint> SeatUserMapping { get; init; } =
        new Dictionary<uint, uint>();

    public string? FailureMessage { get; init; }
}
