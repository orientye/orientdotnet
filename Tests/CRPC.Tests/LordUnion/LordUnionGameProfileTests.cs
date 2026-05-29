using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Scenarios;

namespace CRPC.Tests.LordUnion;

public sealed class LordUnionGameProfileTests
{
    [Fact]
    public void FromConfigCopiesMatchParametersAndVariant()
    {
        var match = new MatchConfig
        {
            GameId = 1001,
            ProductId = 2008280,
            TourneyId = 159740,
        };
        var variant = new ClassicLordVariant();

        var profile = LordUnionGameProfiles.FromConfig(match, variant);

        Assert.Equal("classic", profile.ProfileId);
        Assert.Equal(1001u, profile.GameId);
        Assert.Equal(2008280u, profile.ProductId);
        Assert.Equal(159740u, profile.TourneyId);
        Assert.Equal(2008280u, profile.MatchPoint);
        Assert.Same(variant, profile.Variant);
    }

    [Fact]
    public void FromConfigRejectsNullInputs()
    {
        var match = new MatchConfig();
        var variant = new ClassicLordVariant();

        Assert.Throws<ArgumentNullException>(() => LordUnionGameProfiles.FromConfig(null!, variant));
        Assert.Throws<ArgumentNullException>(() => LordUnionGameProfiles.FromConfig(match, null!));
    }
}
