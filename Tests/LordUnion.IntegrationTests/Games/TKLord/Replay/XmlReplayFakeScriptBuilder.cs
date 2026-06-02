using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;

namespace LordUnion.IntegrationTests.Games.TKLord.Replay;

/// <summary>
/// Builds minimal lord ack sequences for fake-transport XML replay game-flow tests.
/// </summary>
public static class XmlReplayFakeScriptBuilder
{
    public const string DefaultFixtureId = "20260601_7646425803181457480";

    public const uint DefaultMatchId = 900_001;

    public static ProtocolMessage ReadyRequested(uint matchId = DefaultMatchId) =>
        CreateLordAckMessage(matchId, lordAck =>
        {
            lordAck.LordwaitclientreadyAckMsg = new LordWaitClientReadyAck
            {
                Timestamp = 1,
            };
        });

    public static ProtocolMessage InitCardWithTestRecordId(
        string testRecordId,
        uint firstCallSeat = 1,
        byte[]? cards = null,
        uint matchId = DefaultMatchId) =>
        CreateLordAckMessage(matchId, lordAck =>
        {
            lordAck.LordinitcardAckMsg = new LordInitCardAck
            {
                Firstcallseat = firstCallSeat,
                Cards = cards ?? new byte[] { 0x03, 0x13, 0x23 },
                Testrecordid = testRecordId,
            };
        });

    public static ProtocolMessage BidRequested(
        uint nextCallSeat,
        uint curCallSeat,
        uint curScore = 0,
        uint validateScore = 1,
        uint matchId = DefaultMatchId) =>
        CreateLordAckMessage(matchId, lordAck =>
        {
            lordAck.LordcallscoreAckMsg = new LordCallScoreAck
            {
                Curcallseat = curCallSeat,
                Nextcallseat = nextCallSeat,
                Curscore = curScore,
                Validatescore = validateScore,
            };
        });

    public static ProtocolMessage GameFinished(
        uint winSeat,
        int[] scores,
        uint matchId = DefaultMatchId) =>
        CreateLordAckMessage(matchId, lordAck =>
        {
            var resultAck = new LordResultAck
            {
                Winseat = winSeat,
            };
            resultAck.Score.AddRange(scores);
            lordAck.LordresultAckMsg = resultAck;
        });

    public static ProtocolMessage CreateLordAckMessage(uint matchId, Action<LordAckMsg> configure)
    {
        var lordAck = new LordAckMsg
        {
            Matchid = matchId,
        };
        configure(lordAck);

        return new ProtocolMessage
        {
            Header0 = 5001,
            Kind = ProtocolMessageKind.LordAck,
            Acknowledgement = new TKMobileAckMsg
            {
                LordAckMsg = lordAck,
            },
        };
    }
}
