using LordUnion.IntegrationTests.Scenarios;

namespace LordUnion.IntegrationTests.Flows;

public static class SameTableVerifier
{
    public static void Verify(IReadOnlyList<EnterTableStageResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        if (results.Count != 3)
        {
            throw new InvalidOperationException(
                $"Same-table verification requires exactly 3 successful enter-match results, got {results.Count}.");
        }

        var matchId = results[0].MatchId;
        if (results.Any(result => result.MatchId != matchId))
        {
            throw new InvalidOperationException(
                $"Players are not in the same match: expected matchid {matchId}, got [{string.Join(", ", results.Select(result => result.MatchId))}].");
        }

        VerifySeatMapping(results);
        VerifyTableIdentity(results, matchId);
    }

    private static void VerifyTableIdentity(IReadOnlyList<EnterTableStageResult> results, uint matchId)
    {
        var tableIds = results
            .Select(result => result.TableId)
            .Distinct()
            .ToList();
        if (tableIds.Count != 1)
        {
            throw new InvalidOperationException(
                $"Players are not at the same table: table ids [{string.Join(", ", tableIds)}].");
        }

        if (!results.All(result => result.SeatUserMapping.Count > 0))
        {
            return;
        }

        var playerSets = results
            .Select(result => result.SeatUserMapping.Values.ToHashSet())
            .ToList();
        var reference = playerSets[0];
        if (playerSets.Any(set => !set.SetEquals(reference)))
        {
            throw new InvalidOperationException(
                "Players are not at the same table: InitGameTable player lists do not match.");
        }
    }

    private static void VerifySeatMapping(IReadOnlyList<EnterTableStageResult> results)
    {
        var seatOrders = results.Select(result => result.SeatOrder).ToList();
        if (seatOrders.Any(seat => seat > 2))
        {
            throw new InvalidOperationException(
                "Same-table verification requires seatorder in range 0..2 for a 3-player table.");
        }

        if (seatOrders.Distinct().Count() != seatOrders.Count)
        {
            throw new InvalidOperationException(
                $"Duplicate seat assignments detected: [{string.Join(", ", seatOrders)}].");
        }

        var userIds = results
            .Select(result => result.UserId)
            .Where(userId => userId is > 0)
            .Cast<uint>()
            .ToList();
        if (userIds.Count == results.Count && userIds.Distinct().Count() != userIds.Count)
        {
            throw new InvalidOperationException(
                "Duplicate user ids detected across enter-match results.");
        }

        foreach (var result in results)
        {
            if (result.SeatUserMapping.Count == 0 || result.UserId == 0)
            {
                continue;
            }

            if (!result.SeatUserMapping.TryGetValue(result.SeatOrder, out var mappedUserId))
            {
                throw new InvalidOperationException(
                    $"Seat mapping for seat {result.SeatOrder} is missing in InitGameTableAck.");
            }

            if (mappedUserId != result.UserId)
            {
                throw new InvalidOperationException(
                    $"Seat {result.SeatOrder} userid mismatch: EnterRound user {result.UserId}, InitGameTable user {mappedUserId}.");
            }
        }
    }
}