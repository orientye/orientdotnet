using LordUnion.IntegrationTests.Bots;

namespace LordUnion.IntegrationTests.Tests;

public class CardCodecTests
{
    [Theory]
    [InlineData(0, 0, 3)]
    [InlineData(1, 0, 4)]
    [InlineData(12, 0, 15)]
    [InlineData(13, 1, 3)]
    [InlineData(51, 3, 15)]
    [InlineData(52, 5, 16)]
    [InlineData(53, 4, 17)]
    public void Decode_SingleCard_MatchesUnityEncoding(byte cardByte, int expectedColor, int expectedValue)
    {
        var cards = CardCodec.Decode(new[] { cardByte });

        Assert.Single(cards);
        Assert.Equal(cardByte, cards[0].Byte);
        Assert.Equal(expectedColor, cards[0].Color);
        Assert.Equal(expectedValue, cards[0].Value);
    }

    [Fact]
    public void Decode_SortsByValueThenColor()
    {
        // 3♠(39), 3♦(0), 4♦(1), 2♦(12), 小王(52), 大王(53)
        var cards = CardCodec.Decode(new byte[] { 39, 0, 1, 12, 53, 52 });

        Assert.Equal(new byte[] { 0, 39, 1, 12, 52, 53 }, CardCodec.Encode(cards));
    }

    [Fact]
    public void Compare_OrdersRankBeforeSuit()
    {
        var threeDiamond = new GameCard(0);
        var threeSpade = new GameCard(39);
        var fourDiamond = new GameCard(1);

        Assert.True(CardCodec.Compare(threeDiamond, threeSpade) < 0);
        Assert.True(CardCodec.Compare(threeSpade, fourDiamond) < 0);
        Assert.True(CardCodec.Compare(fourDiamond, threeDiamond) > 0);
    }

    [Fact]
    public void Compare_JokersSortAfterTwo()
    {
        var twoDiamond = new GameCard(12);
        var smallJoker = new GameCard(52);
        var bigJoker = new GameCard(53);

        Assert.True(CardCodec.Compare(twoDiamond, smallJoker) < 0);
        Assert.True(CardCodec.Compare(smallJoker, bigJoker) < 0);
    }

    [Fact]
    public void Beats_UsesCompareSemantics()
    {
        var four = new GameCard(1);
        var five = new GameCard(2);

        Assert.True(CardCodec.Beats(five, four));
        Assert.False(CardCodec.Beats(four, five));
        Assert.False(CardCodec.Beats(four, four));
    }

    [Fact]
    public void Encode_Decode_RoundTripPreservesSortedBytes()
    {
        var original = new byte[] { 12, 0, 26, 52, 53, 5 };
        var roundTrip = CardCodec.Encode(CardCodec.Decode(original));

        Assert.Equal(new byte[] { 0, 26, 5, 12, 52, 53 }, roundTrip);
    }

    [Fact]
    public void Encode_MultipleCards_ReturnsSortedBytes()
    {
        var encoded = CardCodec.Encode(new[]
        {
            new GameCard(51),
            new GameCard(0),
            new GameCard(13),
        });

        Assert.Equal(new byte[] { 0, 13, 51 }, encoded);
    }
}
