using LordUnion.IntegrationTests.Bots;
using LordUnion.IntegrationTests.Games.TKLord.Replay;

namespace LordUnion.IntegrationTests.Tests;

public sealed class XmlCardCodecTests
{
    [Fact]
    public void DecodePlayString_MatchesCardCodecBytes()
    {
        var bytes = XmlCardCodec.DecodePlayString("D3S3");
        var cards = CardCodec.Decode(bytes);

        Assert.Equal(2, cards.Count);
        Assert.Equal(new GameCard(0), cards[0]);
        Assert.Equal(new GameCard(39), cards[1]);
    }

    [Fact]
    public void DecodePlayString_MatchesTkLordChar2Card_ForOpeningRun()
    {
        var bytes = XmlCardCodec.DecodePlayString("S9H8C7D6D5S4C3");

        Assert.Equal(7, bytes.Length);
        Assert.Contains((byte)45, bytes);
        Assert.Contains((byte)31, bytes);
    }

    [Fact]
    public void DecodePlayString_PreservesXmlTokenOrder_NotCardCodecSort()
    {
        var bytes = XmlCardCodec.DecodePlayString("HQCQ");

        Assert.Equal(2, bytes.Length);
        Assert.Equal((byte)(2 * 13 + 9), bytes[0]);
        Assert.Equal((byte)(1 * 13 + 9), bytes[1]);
        Assert.NotEqual(CardCodec.Encode([new GameCard(bytes[0]), new GameCard(bytes[1])]), bytes);
    }

    [Fact]
    public void DecodePlayString_SupportsJokers_MatchesChar2Card()
    {
        Assert.Equal(new byte[] { 52 }, XmlCardCodec.DecodePlayString("GL"));
        Assert.Equal(new byte[] { 53 }, XmlCardCodec.DecodePlayString("GB"));
        Assert.Equal(new byte[] { 53, 52 }, XmlCardCodec.DecodePlayString("GBGL"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DecodePlayString_EmptyMeansPass(string value)
    {
        Assert.Empty(XmlCardCodec.DecodePlayString(value));
    }
}
