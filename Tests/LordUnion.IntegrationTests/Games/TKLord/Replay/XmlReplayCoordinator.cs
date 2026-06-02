namespace LordUnion.IntegrationTests.Games.TKLord.Replay;

public sealed class XmlReplayCoordinator
{
    private const int ExpectedSeatCount = 3;

    private readonly string baseDirectory;
    private readonly Dictionary<uint, string?> idsBySeat = new();
    private string? canonicalId;
    private XmlRecordCatalog? catalog;

    public XmlReplayCoordinator(string baseDirectory)
    {
        this.baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
    }

    public bool IsReplayActive => !string.IsNullOrWhiteSpace(canonicalId);

    public XmlRecordCatalog? Catalog => catalog;

    public string? TestRecordId => canonicalId;

    public string? FixturePath => catalog?.FixturePath;

    public void RegisterInitCard(uint seat, string? testRecordId)
    {
        idsBySeat[seat] = string.IsNullOrWhiteSpace(testRecordId) ? null : testRecordId.Trim();

        var values = idsBySeat.Values.ToList();
        var nonEmptyIds = values.Where(id => id is not null).Select(id => id!).Distinct().ToList();
        if (nonEmptyIds.Count > 1)
        {
            throw new InvalidOperationException(
                $"LordInitCardAck testrecordid mismatch across seats: {FormatSeatIds()}.");
        }

        if (nonEmptyIds.Count == 1)
        {
            canonicalId = nonEmptyIds[0];
            catalog ??= XmlRecordCatalog.Load(canonicalId, baseDirectory);
        }

        if (values.Count < ExpectedSeatCount)
        {
            return;
        }

        if (values.Any(id => id is not null) && values.Any(id => id is null))
        {
            throw new InvalidOperationException(
                $"LordInitCardAck testrecordid must be present on all seats when replay is active: {FormatSeatIds()}");
        }
    }

    public XmlReplayBotPolicy CreatePolicy(uint seat) =>
        XmlReplayBotPolicy.CreateWithCoordinator(seat, this);

    private string FormatSeatIds() =>
        string.Join(", ", idsBySeat.Select(pair => $"{pair.Key}={pair.Value ?? "<empty>"}"));
}
