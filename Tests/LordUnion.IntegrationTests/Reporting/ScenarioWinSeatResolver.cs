namespace LordUnion.IntegrationTests.Reporting;

public static class ScenarioWinSeatResolver
{
    /// <summary>
    /// Prefer the first positive seat; if every account reports 0, return 0; otherwise null.
    /// </summary>
    public static uint? ResolveAggregateWinSeat(IEnumerable<uint?> perAccountWinSeats)
    {
        var values = perAccountWinSeats.Where(seat => seat.HasValue).Select(seat => seat!.Value).ToList();
        if (values.Count == 0)
        {
            return null;
        }

        foreach (var seat in values)
        {
            if (seat > 0)
            {
                return seat;
            }
        }

        return values.All(seat => seat == 0) ? 0u : null;
    }
}