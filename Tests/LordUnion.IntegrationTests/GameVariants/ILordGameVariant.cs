using LordUnion.IntegrationTests.Protocol.Generated;

namespace LordUnion.IntegrationTests.GameVariants;

/// <summary>
/// Adapts game-variant protobuf messages to variant-neutral events and requests.
/// </summary>
public interface ILordGameVariant
{
    /// <summary>Stable variant identifier, e.g. <c>"classic"</c> or game id <c>"1001"</c>.</summary>
    string VariantId { get; }

    bool CanHandle(TKMobileAckMsg ack);

    GameEvent? DecodeGameEvent(TKMobileAckMsg ack);

    TKMobileReqMsg BuildReadyReq(uint matchId, uint seat);

    TKMobileReqMsg BuildBidReq(
        uint matchId,
        uint curCallSeat,
        uint nextCallSeat,
        uint curScore,
        uint validateScore);

    TKMobileReqMsg BuildPlayCardsReq(
        uint matchId,
        uint seat,
        uint nextPlayer,
        byte[] cards,
        bool isOver = false);

    /// <summary>
    /// Pass is encoded as <see cref="LordTakeoutCardReq"/> with an empty card array
    /// and <see cref="LordTakeoutCardReq.Passplayer"/> set to the passing seat.
    /// </summary>
    TKMobileReqMsg BuildPassReq(uint matchId, uint seat, uint nextPlayer, uint passPlayer);

    /// <summary>
    /// Used when the server forces a declare-landlord prompt. Classic Dou Dizhu supports this.
    /// </summary>
    TKMobileReqMsg? BuildForceDeclareLoadReq(uint matchId, int seat, int isCall);
}
