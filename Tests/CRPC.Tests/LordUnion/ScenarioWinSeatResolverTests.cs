using LordUnion.IntegrationTests.Reporting;

namespace CRPC.Tests.LordUnion;

public class ScenarioWinSeatResolverTests
{
    [Fact]
    public void ResolveAggregateWinSeat_PrefersPositiveSeat()
    {
        var resolved = ScenarioWinSeatResolver.ResolveAggregateWinSeat([0u, 2u, null]);

        Assert.Equal(2u, resolved);
    }

    [Fact]
    public void ResolveAggregateWinSeat_ReturnsZero_WhenAllAccountsReportZero()
    {
        var resolved = ScenarioWinSeatResolver.ResolveAggregateWinSeat([0u, 0u, 0u]);

        Assert.Equal(0u, resolved);
    }

    [Fact]
    public void ResolveAggregateWinSeat_ReturnsNull_WhenNoSeatValues()
    {
        var resolved = ScenarioWinSeatResolver.ResolveAggregateWinSeat([null, null]);

        Assert.Null(resolved);
    }
}
