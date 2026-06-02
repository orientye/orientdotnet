using LordUnion.IntegrationTests.Games.TKLord.Replay;

namespace LordUnion.IntegrationTests.Tests;

public sealed class XmlRecordParserTests
{
    private static string FixturePath(string stem) =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "Games",
            "TKLord",
            "Cases",
            $"{stem}.xml"));

    [Theory]
    [InlineData("20260601_7646425803181457480", 1u, 0u, 2u)]
    [InlineData("20260601_7646425803181457480", 0u, 0u, 3u)]
    public void Parse_ExtractsBidQueuePerSeat(string stem, uint seat, int bidIndex, uint expectedScore)
    {
        var script = XmlRecordParser.ParseFile(FixturePath(stem));
        var bids = script.BidsBySeat[seat];

        Assert.True(bidIndex < bids.Count);
        Assert.Equal(expectedScore, bids[bidIndex].BidScore);
    }

    [Fact]
    public void Parse_ExtractsPlayActionsForSeat0FirstPlay()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
        var plays = script.PlaysBySeat[0];

        Assert.NotEmpty(plays);
        Assert.Equal("D3S3", plays[0].CardString);
        Assert.False(plays[0].IsPass);
    }

    [Fact]
    public void Parse_IncludesAllPlayActionsRegardlessOfAutoAttribute()
    {
        const string xml = """
            <actions>
            <a id="10" s="0" o="DA" auto="0" />
            <a id="10" s="0" o="" auto="1" />
            <a id="10" s="0" o="C2" auto="0" />
            </actions>
            """;

        var script = XmlRecordParser.Parse(xml, "auto-include-test");
        var plays = script.PlaysBySeat[0];

        Assert.Equal(3, plays.Count);
        Assert.Equal("DA", plays[0].CardString);
        Assert.True(plays[1].IsPass);
        Assert.Equal("C2", plays[2].CardString);
    }

    [Fact]
    public void Parse_AllFiveFixtures_Succeed()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Games", "TKLord", "Cases"));
        foreach (var file in Directory.EnumerateFiles(dir, "*.xml"))
        {
            var script = XmlRecordParser.ParseFile(file);
            Assert.True(script.BidsBySeat.Values.Sum(q => q.Count) >= 2);
            Assert.True(script.PlaysBySeat.Values.Sum(q => q.Count) >= 3);
        }
    }
}

public sealed class XmlRecordCatalogTests
{
    [Fact]
    public void Load_ResolvesFixtureByStem()
    {
        var catalog = XmlRecordCatalog.Load(
            "20260601_7646425803181457480",
            AppContext.BaseDirectory);

        Assert.Equal("20260601_7646425803181457480", catalog.Script.TestRecordId);
        Assert.True(catalog.FixturePath.EndsWith("20260601_7646425803181457480.xml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Load_MissingFixture_ThrowsFileNotFound()
    {
        var ex = Assert.Throws<FileNotFoundException>(() =>
            XmlRecordCatalog.Load("missing_case_id", AppContext.BaseDirectory));

        Assert.Contains("missing_case_id", ex.Message, StringComparison.Ordinal);
    }
}
