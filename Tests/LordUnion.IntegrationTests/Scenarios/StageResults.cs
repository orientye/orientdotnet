namespace LordUnion.IntegrationTests.Scenarios;

public sealed record LoginStageResult(
    int Result,
    uint UserId,
    ulong? SessionId,
    string? Nickname,
    string AesKey);

public sealed record SignupStageResult(
    int MobileResult,
    uint SignupAckParam,
    uint TourneyId,
    uint MatchPoint,
    uint GameId)
{
    public int Result => MobileResult;
}

public sealed record MatchStartStageResult(
    uint MatchId,
    string? ServerIp,
    uint? ServerPort);

public sealed record EnterMatchStageResult(
    uint MatchId,
    uint? TableId);

public sealed record EnterRoundStageResult(
    uint MatchId,
    uint TableId,
    uint Seat);