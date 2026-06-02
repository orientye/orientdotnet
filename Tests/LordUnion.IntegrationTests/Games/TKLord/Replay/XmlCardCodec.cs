using LordUnion.IntegrationTests.Bots;

namespace LordUnion.IntegrationTests.Games.TKLord.Replay;

public static class XmlCardCodec
{
    // Must match TKLordSvr Char2Card() suit mapping (TKLordGame.cpp).
    private static readonly Dictionary<char, int> SuitToColor = new()
    {
        ['S'] = 3,
        ['H'] = 2,
        ['C'] = 1,
        ['D'] = 0,
    };

    private static readonly Dictionary<char, int> RankToValueOffset = new()
    {
        ['3'] = 0,
        ['4'] = 1,
        ['5'] = 2,
        ['6'] = 3,
        ['7'] = 4,
        ['8'] = 5,
        ['9'] = 6,
        ['T'] = 7,
        ['J'] = 8,
        ['Q'] = 9,
        ['K'] = 10,
        ['A'] = 11,
        ['2'] = 12,
    };

    public static byte[] DecodePlayString(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return Array.Empty<byte>();
        }

        var tokens = encoded.Trim();
        var cards = new List<GameCard>();

        for (var i = 0; i < tokens.Length;)
        {
            if (i + 1 >= tokens.Length)
            {
                throw new FormatException($"Incomplete card token at offset {i} in '{encoded}'.");
            }

            var suit = tokens[i];
            var rank = tokens[i + 1];
            i += 2;

            if (suit == 'G')
            {
                cards.Add(new GameCard(rank switch
                {
                    'L' => 52,
                    'B' => 53,
                    _ => throw new FormatException($"Unknown joker rank '{rank}' in '{encoded}'."),
                }));
                continue;
            }

            if (!SuitToColor.TryGetValue(suit, out var color))
            {
                throw new FormatException($"Unknown suit '{suit}' in '{encoded}'.");
            }

            if (!RankToValueOffset.TryGetValue(rank, out var rankOffset))
            {
                throw new FormatException($"Unknown rank '{rank}' in '{encoded}'.");
            }

            var byteValue = (byte)(color * 13 + rankOffset);
            cards.Add(new GameCard(byteValue));
        }

        // Preserve XML token order for wire replay (CardCodec.Encode sorts by rank/suit).
        var bytes = new byte[cards.Count];
        for (var i = 0; i < cards.Count; i++)
        {
            bytes[i] = cards[i].Byte;
        }

        return bytes;
    }
}
