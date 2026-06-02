namespace LordUnion.IntegrationTests.Games.TKLord.Replay;

public sealed class XmlReplayScript
{
    public required string TestRecordId { get; init; }

    public required IReadOnlyDictionary<uint, IReadOnlyList<XmlBidAction>> BidsBySeat { get; init; }

    public required IReadOnlyDictionary<uint, IReadOnlyList<XmlPlayAction>> PlaysBySeat { get; init; }
}
