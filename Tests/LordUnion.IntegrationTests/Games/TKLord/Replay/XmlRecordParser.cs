using System.Text;
using System.Text.RegularExpressions;

namespace LordUnion.IntegrationTests.Games.TKLord.Replay;

public static class XmlRecordParser
{
    private static readonly Regex ActionTagPattern = new(
        @"<a\b[^>]*/?>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IdAttributePattern = new(
        @"\bid=""(\d+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SeatAttributePattern = new(
        @"\bs=""(\d+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ValueAttributePattern = new(
        @"\bo=""([^""]*)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static XmlRecordParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static XmlReplayScript ParseFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"XML replay fixture not found: {path}", path);
        }

        var bytes = File.ReadAllBytes(path);
        var xml = ReadXmlText(bytes);
        return Parse(xml, Path.GetFileNameWithoutExtension(path));
    }

    internal static string ReadXmlText(byte[] bytes)
    {
        try
        {
            return Encoding.GetEncoding(936).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    public static XmlReplayScript Parse(string xml, string testRecordId)
    {
        var bids = new Dictionary<uint, List<XmlBidAction>>
        {
            [0] = new(),
            [1] = new(),
            [2] = new(),
        };
        var plays = new Dictionary<uint, List<XmlPlayAction>>
        {
            [0] = new(),
            [1] = new(),
            [2] = new(),
        };

        foreach (Match tagMatch in ActionTagPattern.Matches(xml))
        {
            var tag = tagMatch.Value;
            var idMatch = IdAttributePattern.Match(tag);
            if (!idMatch.Success)
            {
                continue;
            }

            var id = int.Parse(idMatch.Groups[1].Value);
            if (id != 2 && id != 10)
            {
                continue;
            }

            var seatMatch = SeatAttributePattern.Match(tag);
            if (!seatMatch.Success)
            {
                continue;
            }

            var seat = uint.Parse(seatMatch.Groups[1].Value);
            var valueMatch = ValueAttributePattern.Match(tag);
            var o = valueMatch.Success ? valueMatch.Groups[1].Value : string.Empty;

            if (id == 2)
            {
                bids[seat].Add(new XmlBidAction(uint.Parse(o)));
            }
            else
            {
                plays[seat].Add(new XmlPlayAction(o, string.IsNullOrEmpty(o)));
            }
        }

        return new XmlReplayScript
        {
            TestRecordId = testRecordId,
            BidsBySeat = bids.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<XmlBidAction>)pair.Value),
            PlaysBySeat = plays.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<XmlPlayAction>)pair.Value),
        };
    }
}
