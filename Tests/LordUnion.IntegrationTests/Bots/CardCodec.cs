namespace LordUnion.IntegrationTests.Bots;

/// <summary>
/// Mirrors Unity <c>Card</c> / <c>CardsMgr</c> byte encoding without Unity dependencies.
/// </summary>
public readonly struct GameCard : IEquatable<GameCard>
{
    public byte Byte { get; init; }

    public int Color { get; init; }

    public int Value { get; init; }

    public GameCard(byte b)
    {
        Byte = b;

        if (b > 53)
        {
            Color = 0;
            Value = 3;
        }
        else if (b >= 52)
        {
            Value = 16 + (b - 52);
            Color = b == 52 ? 5 : 4;
        }
        else
        {
            Color = b / 13;
            Value = b % 13 + 3;
        }
    }

    public bool Equals(GameCard other) => Byte == other.Byte;

    public override bool Equals(object? obj) => obj is GameCard other && Equals(other);

    public override int GetHashCode() => Byte.GetHashCode();

    public static bool operator ==(GameCard left, GameCard right) => left.Equals(right);

    public static bool operator !=(GameCard left, GameCard right) => !left.Equals(right);
}

public static class CardCodec
{
    /// <summary>
    /// Compare by rank (<see cref="GameCard.Value"/>), then suit (<see cref="GameCard.Color"/>).
    /// Negative when <paramref name="a"/> sorts before <paramref name="b"/>.
    /// </summary>
    public static int Compare(GameCard a, GameCard b)
    {
        var diff = a.Value - b.Value;
        if (diff == 0)
        {
            diff = a.Color - b.Color;
        }

        return diff;
    }

    public static List<GameCard> Decode(byte[] data)
    {
        var cards = new List<GameCard>(data.Length);
        foreach (var b in data)
        {
            cards.Add(new GameCard(b));
        }

        cards.Sort(Compare);
        return cards;
    }

    public static byte[] Encode(IReadOnlyList<GameCard> cards)
    {
        var sorted = cards.ToList();
        sorted.Sort(Compare);

        var bytes = new byte[sorted.Count];
        for (var i = 0; i < sorted.Count; i++)
        {
            bytes[i] = sorted[i].Byte;
        }

        return bytes;
    }

    public static bool Beats(GameCard challenger, GameCard defender) => Compare(challenger, defender) > 0;
}
