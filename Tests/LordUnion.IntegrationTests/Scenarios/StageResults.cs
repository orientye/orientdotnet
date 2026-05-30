namespace LordUnion.IntegrationTests.Scenarios;

public sealed record LoginStageResult(
    int Result,
    uint UserId,
    ulong? SessionId,
    string? Nickname,
    string AesKey,
    string? FailureMessage = null)
{
    public bool Success => Result == 0 && UserId > 0 && FailureMessage is null;
}

public sealed record SignupStageResult(
    int MobileResult,
    uint SignupAckParam,
    uint TourneyId,
    uint MatchPoint,
    uint GameId,
    string? FailureMessage = null)
{
    public int Result => MobileResult;

    public bool Success => MobileResult == 0 && SignupAckParam == 0 && FailureMessage is null;

    public uint SignupErrorCode => SignupAckParam;

    public uint MobileAckParam => (uint)Math.Max(0, MobileResult);

    public int Flags => 0;
}

public sealed record EnterTableStageResult(
    uint UserId,
    uint MatchId,
    uint TableId,
    uint SeatOrder,
    IReadOnlyDictionary<uint, uint> SeatUserMapping);

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

public sealed record GameStageResult(
    bool Success,
    uint? WinSeat,
    string? EndSignal,
    IReadOnlyList<int>? Scores = null,
    string? FailureMessage = null);