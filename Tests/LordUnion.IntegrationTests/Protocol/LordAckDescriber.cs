using LordUnion.IntegrationTests.Protocol.Generated;

namespace LordUnion.IntegrationTests.Protocol;

public static class LordAckDescriber
{
    public static string Describe(LordAckMsg? lordAck)
    {
        if (lordAck is null)
        {
            return "none";
        }

        if (lordAck.LordwaitclientreadyAckMsg is not null)
        {
            return "LordWaitClientReadyAck";
        }

        if (lordAck.LordgamestartAckMsg is not null)
        {
            return "LordGameStartAck";
        }

        if (lordAck.LordinitcardAckMsg is not null)
        {
            return "LordInitCardAck";
        }

        if (lordAck.LordcallscoreAckMsg is not null)
        {
            return "LordCallScoreAck";
        }

        if (lordAck.LordtipmsgAckMsg is not null)
        {
            return "LordTipMsgAck";
        }

        if (lordAck.LordoperatestartAckMsg is not null)
        {
            return "LordOperateStartAck";
        }

        if (lordAck.LordtakeoutcardAckMsg is not null)
        {
            return "LordTakeoutCardAck";
        }

        if (lordAck.LordinitbottomcardAckMsg is not null)
        {
            return "LordInitBottomCardAck";
        }

        if (lordAck.LordresultAckMsg is { } resultAck)
        {
            var scores = resultAck.Score.Count == 0
                ? "-"
                : string.Join(",", resultAck.Score);
            return $"LordResultAck winseat={resultAck.Winseat} scores=[{scores}]";
        }

        return "LordAck(other)";
    }

    public static bool IsGameReadySignal(LordAckMsg? lordAck)
    {
        return lordAck?.LordwaitclientreadyAckMsg is not null
               || lordAck?.LordgamestartAckMsg is not null;
    }
}