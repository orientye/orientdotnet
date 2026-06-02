using LordUnion.IntegrationTests.Protocol.Generated;

namespace LordUnion.IntegrationTests.GameVariants;

/// <summary>
/// Classic Dou Dizhu (<c>TKLord.proto</c>) adapter for <c>lord_ack_msg</c> / <c>lord_req_msg</c>.
/// Game id <c>1001</c>.
/// </summary>
public sealed class ClassicLordVariant : ILordGameVariant
{
    public const string ClassicVariantId = "classic";
    public const uint ClassicGameId = 1001;

    public string VariantId => ClassicVariantId;

    public bool CanHandle(TKMobileAckMsg ack)
    {
        var lordAck = ack.LordAckMsg;
        if (lordAck == null)
        {
            return false;
        }

        return lordAck.LordwaitclientreadyAckMsg != null
               || lordAck.LordgamestartAckMsg != null
               || lordAck.LordinitcardAckMsg != null
               || lordAck.LordcallscoreAckMsg != null
               || lordAck.LordinitbottomcardAckMsg != null
               || lordAck.LordoperatestartAckMsg != null
               || lordAck.LordkickAckMsg != null
               || lordAck.LordoperateresultAckMsg != null
               || lordAck.LordtakeoutcardAckMsg != null
               || lordAck.LordresultAckMsg != null;
    }

    public GameEvent? DecodeGameEvent(TKMobileAckMsg ack)
    {
        var lordAck = ack.LordAckMsg;
        if (lordAck == null)
        {
            return null;
        }

        var matchId = lordAck.Matchid;

        if (lordAck.LordwaitclientreadyAckMsg is { } readyAck)
        {
            return new GameEvent
            {
                Kind = GameEventKind.ReadyRequested,
                MatchId = matchId,
                Timestamp = readyAck.Timestamp,
            };
        }

        if (lordAck.LordgamestartAckMsg is { } startAck)
        {
            return new GameEvent
            {
                Kind = GameEventKind.GameStarted,
                MatchId = matchId,
                Timestamp = startAck.TimeStamp,
            };
        }

        if (lordAck.LordinitcardAckMsg is { } initCardAck)
        {
            return new GameEvent
            {
                Kind = GameEventKind.CardsDealt,
                MatchId = matchId,
                FirstCallSeat = initCardAck.Firstcallseat,
                Cards = initCardAck.Cards,
                TestRecordId = initCardAck.Testrecordid,
            };
        }

        if (lordAck.LordcallscoreAckMsg is { } callScoreAck)
        {
            return new GameEvent
            {
                Kind = GameEventKind.BidRequested,
                MatchId = matchId,
                CurCallSeat = callScoreAck.Curcallseat,
                NextCallSeat = callScoreAck.Nextcallseat,
                CurScore = callScoreAck.Curscore,
                ValidateScore = callScoreAck.Validatescore,
            };
        }

        if (lordAck.LordinitbottomcardAckMsg is { } bottomCardAck)
        {
            return new GameEvent
            {
                Kind = GameEventKind.LandlordDeclared,
                MatchId = matchId,
                LordSeat = bottomCardAck.Lordseat,
                Cards = bottomCardAck.Cards,
            };
        }

        if (lordAck.LordoperatestartAckMsg is { } operateStartAck)
        {
            return new GameEvent
            {
                Kind = GameEventKind.TurnStarted,
                MatchId = matchId,
                Timestamp = operateStartAck.TimeStamp,
                OperateTypes = operateStartAck.OperateType.ToList(),
                SeatList = operateStartAck.Seatlist.ToList(),
            };
        }

        if (lordAck.LordkickAckMsg is { } kickAck)
        {
            return new GameEvent
            {
                Kind = GameEventKind.KickAck,
                MatchId = matchId,
                Seat = kickAck.Seat,
                Kick = kickAck.Kick,
            };
        }

        if (lordAck.LordoperateresultAckMsg is { } operateResultAck)
        {
            return new GameEvent
            {
                Kind = GameEventKind.OperateFinished,
                MatchId = matchId,
                Timestamp = operateResultAck.Timestamp,
                OperateTypes = operateResultAck.OperateType.ToList(),
            };
        }

        if (lordAck.LordtakeoutcardAckMsg is { } takeoutAck)
        {
            var cards = takeoutAck.Cards;
            var isPass = cards.Length == 0;

            return new GameEvent
            {
                Kind = isPass ? GameEventKind.PassPlayed : GameEventKind.CardsPlayed,
                MatchId = matchId,
                Seat = takeoutAck.Seat,
                NextPlayer = takeoutAck.Nextplayer,
                PassPlayer = takeoutAck.Passplayer,
                Cards = cards,
                NextAutoPass = takeoutAck.Nextautopass ? true : null,
                NextAutoGo = takeoutAck.Nextautogo ? true : null,
                TakeoutMsgCnt = takeoutAck.Msgcnt > 0 ? takeoutAck.Msgcnt : null,
            };
        }

        if (lordAck.LordresultAckMsg is { } resultAck)
        {
            return new GameEvent
            {
                Kind = GameEventKind.GameFinished,
                MatchId = matchId,
                WinSeat = resultAck.Winseat,
                Scores = resultAck.Score.ToList(),
            };
        }

        return null;
    }

    public TKMobileReqMsg BuildReadyReq(uint matchId, uint seat)
    {
        return WrapLordReq(matchId, lordReq =>
        {
            lordReq.LordclientreadyReqMsg = new LordClientReadyReq
            {
                Seat = seat,
            };
        });
    }

    public TKMobileReqMsg BuildBidReq(
        uint matchId,
        uint curCallSeat,
        uint nextCallSeat,
        uint curScore,
        uint validateScore)
    {
        return WrapLordReq(matchId, lordReq =>
        {
            lordReq.LordcallscoreReqMsg = new LordCallScoreReq
            {
                Curcallseat = curCallSeat,
                Nextcallseat = nextCallSeat,
                Curscore = curScore,
                Validatescore = validateScore,
            };
        });
    }

    public TKMobileReqMsg BuildPlayCardsReq(
        uint matchId,
        uint seat,
        uint nextPlayer,
        byte[] cards,
        bool isOver = false,
        uint msgCnt = 0)
    {
        return WrapLordReq(matchId, lordReq =>
        {
            var req = new LordTakeoutCardReq
            {
                Seat = seat,
                Nextplayer = nextPlayer,
                Cards = cards,
                Isover = isOver,
            };
            if (msgCnt > 0)
            {
                req.Msgcnt = msgCnt;
            }

            lordReq.LordtakeoutcardReqMsg = req;
        });
    }

    public TKMobileReqMsg BuildPassReq(uint matchId, uint seat, uint nextPlayer, uint passPlayer, uint msgCnt = 0)
    {
        // Classic pass uses LordTakeoutCardReq with empty cards (same wire shape as play).
        return WrapLordReq(matchId, lordReq =>
        {
            var req = new LordTakeoutCardReq
            {
                Seat = seat,
                Nextplayer = nextPlayer,
                Passplayer = passPlayer,
                Cards = Array.Empty<byte>(),
                Isover = false,
            };
            if (msgCnt > 0)
            {
                req.Msgcnt = msgCnt;
            }

            lordReq.LordtakeoutcardReqMsg = req;
        });
    }

    public TKMobileReqMsg BuildKickReq(uint matchId, uint seat, bool kick) =>
        WrapLordReq(matchId, lordReq =>
        {
            lordReq.LordKickReqMsg = new LordKickReq
            {
                Seat = seat,
                Kick = kick,
            };
        });

    public TKMobileReqMsg? BuildForceDeclareLoadReq(uint matchId, int seat, int isCall)
    {
        return WrapLordReq(matchId, lordReq =>
        {
            lordReq.LordforcedeclareloadReqMsg = new LordForceDeclareLoadReq
            {
                Seat = seat,
                Iscall = isCall,
            };
        });
    }

    private static TKMobileReqMsg WrapLordReq(uint matchId, Action<LordReqMsg> configure)
    {
        var lordReq = new LordReqMsg
        {
            Matchid = matchId,
        };
        configure(lordReq);

        return new TKMobileReqMsg
        {
            LordReqMsg = lordReq,
        };
    }
}