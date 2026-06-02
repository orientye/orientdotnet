namespace LordUnion.IntegrationTests.Games.TKLord.Replay;

public sealed record XmlRecordCatalog(XmlReplayScript Script, string FixturePath)
{
    public static XmlRecordCatalog Load(string testRecordId, string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testRecordId);

        var fixturePath = Path.GetFullPath(Path.Combine(
            baseDirectory,
            "Games",
            "TKLord",
            "Cases",
            $"{testRecordId}.xml"));

        var script = XmlRecordParser.ParseFile(fixturePath);
        return new XmlRecordCatalog(script, fixturePath);
    }
}
