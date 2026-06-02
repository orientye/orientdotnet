using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Protocol.Generated;

namespace LordUnion.IntegrationTests.Bots;

public enum BotDecisionKind
{
    Ready,
    Bid,
    Kick,
    Play,
    Pass,
}

public sealed record BotDecision
{
    public required BotDecisionKind Kind { get; init; }

    public uint? CurCallSeat { get; init; }

    public uint? NextCallSeat { get; init; }

    public uint? CurScore { get; init; }

    public uint? ValidateScore { get; init; }

    public byte[]? Cards { get; init; }

    public uint? NextPlayer { get; init; }

    public int? PassPlayer { get; init; }

    /// <summary>Farmer double (kick); XML replay uses <c>false</c> (no double).</summary>
    public bool? Kick { get; init; }

    public static BotDecision Ready() =>
        new() { Kind = BotDecisionKind.Ready };

    public static BotDecision Bid(uint curCallSeat, uint nextCallSeat, uint validateScore, uint curScore = 0) =>
        new()
        {
            Kind = BotDecisionKind.Bid,
            CurCallSeat = curCallSeat,
            NextCallSeat = nextCallSeat,
            CurScore = curScore,
            ValidateScore = validateScore,
        };

    public static BotDecision Play(uint nextPlayer, byte[] cards) =>
        new()
        {
            Kind = BotDecisionKind.Play,
            NextPlayer = nextPlayer,
            Cards = cards,
        };

    public static BotDecision Pass(uint nextPlayer, int passPlayer) =>
        new()
        {
            Kind = BotDecisionKind.Pass,
            NextPlayer = nextPlayer,
            PassPlayer = passPlayer,
        };

    public static BotDecision DeclineKick() =>
        new() { Kind = BotDecisionKind.Kick, Kick = false };

    public TKMobileReqMsg ToRequest(ClassicLordVariant variant, uint matchId, uint seat, uint takeoutMsgCnt = 0)
    {
        return Kind switch
        {
            BotDecisionKind.Ready => variant.BuildReadyReq(matchId, seat),
            BotDecisionKind.Bid => variant.BuildBidReq(
                matchId,
                CurCallSeat ?? seat,
                NextCallSeat ?? seat,
                CurScore ?? 0,
                ValidateScore ?? 0),
            BotDecisionKind.Play => variant.BuildPlayCardsReq(
                matchId,
                seat,
                NextPlayer ?? seat,
                Cards ?? Array.Empty<byte>(),
                msgCnt: takeoutMsgCnt),
            BotDecisionKind.Pass => variant.BuildPassReq(
                matchId,
                seat,
                NextPlayer ?? seat,
                (uint)(PassPlayer ?? (int)seat),
                msgCnt: takeoutMsgCnt),
            BotDecisionKind.Kick => variant.BuildKickReq(matchId, seat, Kick ?? false),
            _ => throw new InvalidOperationException($"Unsupported decision kind: {Kind}"),
        };
    }
}