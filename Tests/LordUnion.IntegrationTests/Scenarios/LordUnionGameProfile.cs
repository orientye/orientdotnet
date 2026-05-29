using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.GameVariants;

namespace LordUnion.IntegrationTests.Scenarios;

public sealed record LordUnionGameProfile
{
    public string ProfileId { get; init; } = "classic";

    public uint GameId { get; init; }

    public uint ProductId { get; init; }

    public uint TourneyId { get; init; }

    public uint MatchPoint { get; init; }

    public required ILordGameVariant Variant { get; init; }
}

public static class LordUnionGameProfiles
{
    public static LordUnionGameProfile FromConfig(MatchConfig match, ILordGameVariant variant)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(variant);

        return new LordUnionGameProfile
        {
            ProfileId = variant.VariantId,
            GameId = match.GameId,
            ProductId = match.ProductId,
            TourneyId = match.TourneyId,
            MatchPoint = match.ProductId,
            Variant = variant,
        };
    }
}