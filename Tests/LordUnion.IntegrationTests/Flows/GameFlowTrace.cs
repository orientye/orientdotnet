using LordUnion.IntegrationTests.GameVariants;

namespace LordUnion.IntegrationTests.Flows;

/// <summary>
/// Optional diagnostics for game-loop completion. Set <c>LORDUNION_TRACE=1</c> to enable.
/// </summary>
internal static class GameFlowTrace
{
    private static readonly bool Enabled =
        string.Equals(Environment.GetEnvironmentVariable("LORDUNION_TRACE"), "1", StringComparison.Ordinal);

    public static void LogGameEnd(string accountAlias, string endSignal, uint? winSeat)
    {
        if (!Enabled)
        {
            return;
        }

        var seatPart = winSeat is uint seat ? $" winSeat={seat}" : string.Empty;
        Console.WriteLine($"[LordUnion] GameFlow end account={accountAlias} signal={endSignal}{seatPart}");
    }

    public static void LogSeatResolved(
        string accountAlias,
        uint userId,
        uint resolvedSeat,
        uint? enterRoundSeat,
        uint? initListIndex,
        uint? addGamePlayerSeat)
    {
        if (!Enabled)
        {
            return;
        }

        Console.WriteLine(
            $"[LordUnion] seat resolved account={accountAlias} userId={userId} seat={resolvedSeat}"
            + $" enterRound={enterRoundSeat} initListIndex={initListIndex} addGamePlayer={addGamePlayerSeat}");
    }

    public static void LogGameFlowStart(
        string accountAlias,
        uint? userId,
        uint seat,
        uint? matchId)
    {
        if (!Enabled)
        {
            return;
        }

        Console.WriteLine(
            $"[LordUnion] gameflow start account={accountAlias} userId={userId} seat={seat} matchId={matchId}");
    }

    public static void LogCardsDealt(
        string accountAlias,
        uint? userId,
        uint seat,
        string? testRecordId,
        uint? firstCallSeat)
    {
        if (!Enabled)
        {
            return;
        }

        Console.WriteLine(
            $"[LordUnion] init account={accountAlias} userId={userId} seat={seat} testrecordid={testRecordId ?? "<none>"} firstCallSeat={firstCallSeat}");
    }

    public static void LogOperateStart(
        string accountAlias,
        uint? userId,
        uint mySeat,
        IReadOnlyList<uint>? operateTypes,
        IReadOnlyList<uint>? seatList)
    {
        if (!Enabled)
        {
            return;
        }

        var typesPart = operateTypes is null ? "<none>" : string.Join(',', operateTypes);
        var seatsPart = seatList is null ? "<none>" : string.Join(',', seatList);
        var inList = seatList?.Contains(mySeat) == true;
        Console.WriteLine(
            $"[LordUnion] operateStart account={accountAlias} userId={userId} mySeat={mySeat}"
            + $" operateTypes={typesPart} seatList={seatsPart} inList={inList}");
    }

    public static void LogKickAck(
        string accountAlias,
        uint? userId,
        uint seat,
        bool kick)
    {
        if (!Enabled)
        {
            return;
        }

        Console.WriteLine(
            $"[LordUnion] kickAck account={accountAlias} userId={userId} seat={seat} kick={kick}");
    }

    public static void LogTakeoutAck(
        string accountAlias,
        uint? userId,
        uint mySeat,
        GameEventKind kind,
        uint? ackSeat,
        uint? nextPlayer,
        uint? msgCnt,
        int cardCount,
        bool? nextAutoPass,
        bool? nextAutoGo,
        int? passPlayer = null)
    {
        if (!Enabled)
        {
            return;
        }

        var passPart = passPlayer is int passSeat ? $" passPlayer={passSeat}" : string.Empty;
        Console.WriteLine(
            $"[LordUnion] takeoutAck account={accountAlias} userId={userId} mySeat={mySeat} kind={kind}"
            + $" ackSeat={ackSeat} nextPlayer={nextPlayer} msgCnt={msgCnt} cardCount={cardCount}"
            + $" nextAutoPass={nextAutoPass} nextAutoGo={nextAutoGo}{passPart}");
    }

    public static void LogSendDecision(
        string accountAlias,
        uint? userId,
        uint reqSeat,
        string decisionKind,
        string trigger,
        uint? lordSeat,
        int? playIndex,
        string? xmlCards,
        int? encodedCount,
        uint? takeoutMsgCnt = null)
    {
        if (!Enabled)
        {
            return;
        }

        var lordPart = lordSeat is uint lord ? $" lordSeat={lord}" : string.Empty;
        var indexPart = playIndex is int index ? $" playIndex={index}" : string.Empty;
        var xmlPart = xmlCards is null ? string.Empty : $" xml=\"{xmlCards}\"";
        var countPart = encodedCount is int count ? $" cardCount={count}" : string.Empty;
        var msgCntPart = takeoutMsgCnt is uint cnt && cnt > 0 ? $" reqMsgCnt={cnt}" : string.Empty;
        Console.WriteLine(
            $"[LordUnion] send account={accountAlias} userId={userId} reqSeat={reqSeat} decision={decisionKind} trigger={trigger}{lordPart}{indexPart}{xmlPart}{countPart}{msgCntPart}");
    }
}
