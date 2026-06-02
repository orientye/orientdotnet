using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using ProtoBuf;

namespace LordUnion.IntegrationTests.Tests;

public class ClassicLordVariantTests
{
    private readonly ClassicLordVariant _variant = new();
    private readonly ServerProtocolCodec _codec = new();

    private const uint MatchId = 900001;

    [Fact]
    public void VariantId_IsClassic()
    {
        Assert.Equal("classic", _variant.VariantId);
    }

    [Theory]
    [InlineData(typeof(LordWaitClientReadyAck), GameEventKind.ReadyRequested)]
    [InlineData(typeof(LordGameStartAck), GameEventKind.GameStarted)]
    [InlineData(typeof(LordInitCardAck), GameEventKind.CardsDealt)]
    [InlineData(typeof(LordCallScoreAck), GameEventKind.BidRequested)]
    [InlineData(typeof(LordInitBottomCardAck), GameEventKind.LandlordDeclared)]
    [InlineData(typeof(LordOperateStartAck), GameEventKind.TurnStarted)]
    [InlineData(typeof(LordKickAck), GameEventKind.KickAck)]
    [InlineData(typeof(LordOperateResultAck), GameEventKind.OperateFinished)]
    [InlineData(typeof(LordResultAck), GameEventKind.GameFinished)]
    public void CanHandle_SupportedClassicAck_ReturnsTrue(Type ackType, GameEventKind _)
    {
        var ack = CreateAckWithSupportedField(ackType);

        Assert.True(_variant.CanHandle(ack));
    }

    [Fact]
    public void CanHandle_TakeoutWithCards_ReturnsTrue()
    {
        var ack = CreateLordAck(lordAck =>
        {
            lordAck.LordtakeoutcardAckMsg = new LordTakeoutCardAck
            {
                Seat = 1,
                Nextplayer = 2,
                Passplayer = 0,
                Isover = false,
                Cards = new byte[] { 0x11, 0x22 },
            };
        });

        Assert.True(_variant.CanHandle(ack));
    }

    [Fact]
    public void CanHandle_EmptyAck_ReturnsFalse()
    {
        Assert.False(_variant.CanHandle(new TKMobileAckMsg()));
    }

    [Fact]
    public void CanHandle_NonLordAck_ReturnsFalse()
    {
        var ack = new TKMobileAckMsg
        {
            LobbyAckMsg = new LobbyAckMsg
            {
                AnonymousAckMsg = new AnonymousBrowseAck(),
            },
        };

        Assert.False(_variant.CanHandle(ack));
    }

    [Fact]
    public void CanHandle_UnknownLordAck_ReturnsFalse()
    {
        var ack = CreateLordAck(lordAck =>
        {
            lordAck.LordemptyAckMsg = new LordEmptyAck();
        });

        Assert.False(_variant.CanHandle(ack));
    }

    [Fact]
    public void CanHandle_VariantLzLordAck_ReturnsFalse()
    {
        var ack = new TKMobileAckMsg
        {
            LzlordAckMsg = new LZLordAckMsg
            {
                Matchid = MatchId,
                LordwaitclientreadyAckMsg = new LordWaitClientReadyAck(),
            },
        };

        Assert.False(_variant.CanHandle(ack));
    }

    [Fact]
    public void DecodeGameEvent_ReadyRequested_MapsTimestamp()
    {
        const ulong timestamp = 1700000000123;
        var ack = CreateLordAck(lordAck =>
        {
            lordAck.LordwaitclientreadyAckMsg = new LordWaitClientReadyAck
            {
                Timestamp = timestamp,
            };
        });

        var gameEvent = _variant.DecodeGameEvent(ack);

        Assert.NotNull(gameEvent);
        Assert.Equal(GameEventKind.ReadyRequested, gameEvent!.Kind);
        Assert.Equal(MatchId, gameEvent.MatchId);
        Assert.Equal(timestamp, gameEvent.Timestamp);
    }

    [Fact]
    public void DecodeGameEvent_GameStarted_MapsTimestamp()
    {
        const ulong timestamp = 1700000000456;
        var ack = CreateLordAck(lordAck =>
        {
            lordAck.LordgamestartAckMsg = new LordGameStartAck
            {
                TimeStamp = timestamp,
            };
        });

        var gameEvent = _variant.DecodeGameEvent(ack);

        Assert.NotNull(gameEvent);
        Assert.Equal(GameEventKind.GameStarted, gameEvent!.Kind);
        Assert.Equal(MatchId, gameEvent.MatchId);
        Assert.Equal(timestamp, gameEvent.Timestamp);
    }

    [Fact]
    public void DecodeGameEvent_CardsDealt_MapsCardsAndFirstCallSeat()
    {
        var cards = new byte[] { 1, 2, 3, 4, 5 };
        var ack = CreateLordAck(lordAck =>
        {
            lordAck.LordinitcardAckMsg = new LordInitCardAck
            {
                Firstcallseat = 2,
                Cards = cards,
            };
        });

        var gameEvent = _variant.DecodeGameEvent(ack);

        Assert.NotNull(gameEvent);
        Assert.Equal(GameEventKind.CardsDealt, gameEvent!.Kind);
        Assert.Equal(MatchId, gameEvent.MatchId);
        Assert.Equal(2u, gameEvent.FirstCallSeat);
        Assert.Equal(cards, gameEvent.Cards);
    }

    [Fact]
    public void DecodeGameEvent_CardsDealt_IncludesTestRecordId()
    {
        var ack = CreateLordAck(lordAck =>
        {
            lordAck.LordinitcardAckMsg = new LordInitCardAck
            {
                Firstcallseat = 1,
                Cards = new byte[] { 0, 1, 2 },
                Testrecordid = "20260601_7646425803181457480",
            };
        });

        var gameEvent = _variant.DecodeGameEvent(ack);

        Assert.NotNull(gameEvent);
        Assert.Equal(GameEventKind.CardsDealt, gameEvent!.Kind);
        Assert.Equal("20260601_7646425803181457480", gameEvent.TestRecordId);
    }

    [Fact]
    public void DecodeGameEvent_BidRequested_MapsBidFields()
    {
        var ack = CreateLordAck(lordAck =>
        {
            lordAck.LordcallscoreAckMsg = new LordCallScoreAck
            {
                Curcallseat = 1,
                Nextcallseat = 2,
                Curscore = 1,
                Validatescore = 3,
            };
        });

        var gameEvent = _variant.DecodeGameEvent(ack);

        Assert.NotNull(gameEvent);
        Assert.Equal(GameEventKind.BidRequested, gameEvent!.Kind);
        Assert.Equal(MatchId, gameEvent.MatchId);
        Assert.Equal(1u, gameEvent.CurCallSeat);
        Assert.Equal(2u, gameEvent.NextCallSeat);
        Assert.Equal(1u, gameEvent.CurScore);
        Assert.Equal(3u, gameEvent.ValidateScore);
    }

    [Fact]
    public void DecodeGameEvent_LandlordDeclared_MapsLordSeatAndBottomCards()
    {
        var bottomCards = new byte[] { 0x33, 0x44, 0x55 };
        var ack = CreateLordAck(lordAck =>
        {
            lordAck.LordinitbottomcardAckMsg = new LordInitBottomCardAck
            {
                Lordseat = 3,
                Cards = bottomCards,
            };
        });

        var gameEvent = _variant.DecodeGameEvent(ack);

        Assert.NotNull(gameEvent);
        Assert.Equal(GameEventKind.LandlordDeclared, gameEvent!.Kind);
        Assert.Equal(MatchId, gameEvent.MatchId);
        Assert.Equal(3u, gameEvent.LordSeat);
        Assert.Equal(bottomCards, gameEvent.Cards);
    }

    [Fact]
    public void DecodeGameEvent_TurnStarted_MapsOperateTypeAndSeatList()
    {
        var ack = CreateLordAck(lordAck =>
        {
            lordAck.LordoperatestartAckMsg = new LordOperateStartAck
            {
                TimeStamp = 999,
                OperateType = { 1, 2 },
                Seatlist = { 1, 2, 3 },
            };
        });

        var gameEvent = _variant.DecodeGameEvent(ack);

        Assert.NotNull(gameEvent);
        Assert.Equal(GameEventKind.TurnStarted, gameEvent!.Kind);
        Assert.Equal(MatchId, gameEvent.MatchId);
        Assert.Equal(999ul, gameEvent.Timestamp);
        Assert.Equal(new uint[] { 1, 2 }, gameEvent.OperateTypes);
        Assert.Equal(new uint[] { 1, 2, 3 }, gameEvent.SeatList);
    }

    [Fact]
    public void DecodeGameEvent_OperateFinished_MapsOperateTypes()
    {
        var ack = CreateLordAck(lordAck =>
        {
            lordAck.LordoperateresultAckMsg = new LordOperateResultAck
            {
                Timestamp = 888,
                OperateType = { 1 },
            };
        });

        var gameEvent = _variant.DecodeGameEvent(ack);

        Assert.NotNull(gameEvent);
        Assert.Equal(GameEventKind.OperateFinished, gameEvent!.Kind);
        Assert.Equal(MatchId, gameEvent.MatchId);
        Assert.Equal(888ul, gameEvent.Timestamp);
        Assert.Equal(new uint[] { 1 }, gameEvent.OperateTypes);
    }

    [Fact]
    public void DecodeGameEvent_KickAck_MapsSeatAndKick()
    {
        var ack = CreateLordAck(lordAck =>
        {
            lordAck.LordkickAckMsg = new LordKickAck
            {
                Seat = 1,
                Kick = false,
            };
        });

        var gameEvent = _variant.DecodeGameEvent(ack);

        Assert.NotNull(gameEvent);
        Assert.Equal(GameEventKind.KickAck, gameEvent!.Kind);
        Assert.Equal(MatchId, gameEvent.MatchId);
        Assert.Equal(1u, gameEvent.Seat);
        Assert.False(gameEvent.Kick);
    }

    [Fact]
    public void DecodeGameEvent_TakeoutWithCards_MapsCardsPlayed()
    {
        var cards = new byte[] { 0x0A, 0x0B };
        var ack = CreateLordAck(lordAck =>
        {
            lordAck.LordtakeoutcardAckMsg = new LordTakeoutCardAck
            {
                Seat = 2,
                Nextplayer = 3,
                Passplayer = 0,
                Isover = false,
                Cards = cards,
                Msgcnt = 42,
            };
        });

        var gameEvent = _variant.DecodeGameEvent(ack);

        Assert.NotNull(gameEvent);
        Assert.Equal(GameEventKind.CardsPlayed, gameEvent!.Kind);
        Assert.Equal(MatchId, gameEvent.MatchId);
        Assert.Equal(2u, gameEvent.Seat);
        Assert.Equal(3u, gameEvent.NextPlayer);
        Assert.Equal(0, gameEvent.PassPlayer);
        Assert.Equal(cards, gameEvent.Cards);
        Assert.Equal(42u, gameEvent.TakeoutMsgCnt);
    }

    [Fact]
    public void DecodeGameEvent_TakeoutWithEmptyCards_MapsPassPlayed()
    {
        var ack = CreateLordAck(lordAck =>
        {
            lordAck.LordtakeoutcardAckMsg = new LordTakeoutCardAck
            {
                Seat = 3,
                Nextplayer = 1,
                Passplayer = 3,
                Isover = false,
                Cards = Array.Empty<byte>(),
            };
        });

        var gameEvent = _variant.DecodeGameEvent(ack);

        Assert.NotNull(gameEvent);
        Assert.Equal(GameEventKind.PassPlayed, gameEvent!.Kind);
        Assert.Equal(MatchId, gameEvent.MatchId);
        Assert.Equal(3u, gameEvent.Seat);
        Assert.Equal(1u, gameEvent.NextPlayer);
        Assert.Equal(3, gameEvent.PassPlayer);
        Assert.Empty(gameEvent.Cards!);
    }

    [Fact]
    public void DecodeGameEvent_GameFinished_MapsWinSeatAndScores()
    {
        var ack = CreateLordAck(lordAck =>
        {
            lordAck.LordresultAckMsg = new LordResultAck
            {
                Winseat = 2,
                Score = { 6, -3, -3 },
            };
        });

        var gameEvent = _variant.DecodeGameEvent(ack);

        Assert.NotNull(gameEvent);
        Assert.Equal(GameEventKind.GameFinished, gameEvent!.Kind);
        Assert.Equal(MatchId, gameEvent.MatchId);
        Assert.Equal(2u, gameEvent.WinSeat);
        Assert.Equal(new[] { 6, -3, -3 }, gameEvent.Scores);
    }

    [Fact]
    public void DecodeGameEvent_UnknownLordAck_ReturnsNull()
    {
        var ack = CreateLordAck(lordAck =>
        {
            lordAck.LordemptyAckMsg = new LordEmptyAck();
        });

        Assert.Null(_variant.DecodeGameEvent(ack));
    }

    [Fact]
    public void DecodeGameEvent_NonLordAck_ReturnsNull()
    {
        Assert.Null(_variant.DecodeGameEvent(new TKMobileAckMsg()));
    }

    [Fact]
    public void BuildReadyReq_WrapsLordClientReadyReq()
    {
        var request = _variant.BuildReadyReq(MatchId, seat: 2);

        Assert.Equal(MatchId, request.LordReqMsg!.Matchid);
        Assert.NotNull(request.LordReqMsg.LordclientreadyReqMsg);
        Assert.Equal(2u, request.LordReqMsg.LordclientreadyReqMsg!.Seat);
    }

    [Fact]
    public void BuildReadyReq_MatchesServerProtocolCodecFactory()
    {
        var variantRequest = _variant.BuildReadyReq(MatchId, seat: 1);
        var codecRequest = _codec.CreateLordClientReadyRequest(MatchId, seat: 1);

        Assert.Equal(codecRequest.LordReqMsg!.Matchid, variantRequest.LordReqMsg!.Matchid);
        Assert.Equal(
            codecRequest.LordReqMsg.LordclientreadyReqMsg!.Seat,
            variantRequest.LordReqMsg.LordclientreadyReqMsg!.Seat);
    }

    [Fact]
    public void BuildReadyReq_RoundTripsThroughCodec()
    {
        var request = _variant.BuildReadyReq(MatchId, seat: 1);
        var packet = _codec.EncodeClientRequest(request);
        var decoded = _codec.DecodePacket(packet);

        Assert.Equal(ProtocolMessageKind.LordReq, decoded.Kind);
        Assert.Equal(MatchId, decoded.LordRequest?.Matchid);
        Assert.Equal(1u, decoded.LordRequest?.LordclientreadyReqMsg?.Seat);
    }

    [Fact]
    public void BuildBidReq_WrapsLordCallScoreReq()
    {
        var request = _variant.BuildBidReq(MatchId, curCallSeat: 1, nextCallSeat: 2, curScore: 2, validateScore: 3);

        Assert.Equal(MatchId, request.LordReqMsg!.Matchid);
        var bidReq = request.LordReqMsg.LordcallscoreReqMsg;
        Assert.NotNull(bidReq);
        Assert.Equal(1u, bidReq!.Curcallseat);
        Assert.Equal(2u, bidReq.Nextcallseat);
        Assert.Equal(2u, bidReq.Curscore);
        Assert.Equal(3u, bidReq.Validatescore);
    }

    [Fact]
    public void BuildPlayCardsReq_WrapsLordTakeoutCardReq()
    {
        var cards = new byte[] { 0x01, 0x02 };
        var request = _variant.BuildPlayCardsReq(MatchId, seat: 1, nextPlayer: 2, cards, isOver: true);

        Assert.Equal(MatchId, request.LordReqMsg!.Matchid);
        var playReq = request.LordReqMsg.LordtakeoutcardReqMsg;
        Assert.NotNull(playReq);
        Assert.Equal(1u, playReq!.Seat);
        Assert.Equal(2u, playReq.Nextplayer);
        Assert.Equal(cards, playReq.Cards);
        Assert.True(playReq.Isover);
    }

    [Fact]
    public void BuildPlayCardsReq_WithMsgCnt_SetsMsgcntOnReq()
    {
        var cards = new byte[] { 0x01 };
        var request = _variant.BuildPlayCardsReq(MatchId, seat: 1, nextPlayer: 2, cards, isOver: false, msgCnt: 7);

        Assert.Equal(7u, request.LordReqMsg!.LordtakeoutcardReqMsg!.Msgcnt);
    }

    [Fact]
    public void BuildPassReq_UsesEmptyCardsAndPassPlayer()
    {
        var request = _variant.BuildPassReq(MatchId, seat: 2, nextPlayer: 3, passPlayer: 2);

        Assert.Equal(MatchId, request.LordReqMsg!.Matchid);
        var passReq = request.LordReqMsg.LordtakeoutcardReqMsg;
        Assert.NotNull(passReq);
        Assert.Equal(2u, passReq!.Seat);
        Assert.Equal(3u, passReq.Nextplayer);
        Assert.Equal(2u, passReq.Passplayer);
        Assert.Empty(passReq.Cards);
        Assert.False(passReq.Isover);
    }

    [Fact]
    public void BuildKickReq_WrapsLordKickReq()
    {
        var request = _variant.BuildKickReq(MatchId, seat: 1, kick: false);

        Assert.Equal(MatchId, request.LordReqMsg!.Matchid);
        var kickReq = request.LordReqMsg.LordKickReqMsg;
        Assert.NotNull(kickReq);
        Assert.Equal(1u, kickReq!.Seat);
        Assert.False(kickReq.Kick);
    }

    [Fact]
    public void BuildForceDeclareLoadReq_WrapsLordForceDeclareLoadReq()
    {
        var request = _variant.BuildForceDeclareLoadReq(MatchId, seat: 1, isCall: 1);

        Assert.NotNull(request);
        Assert.Equal(MatchId, request!.LordReqMsg!.Matchid);
        var forceReq = request.LordReqMsg.LordforcedeclareloadReqMsg;
        Assert.NotNull(forceReq);
        Assert.Equal(1, forceReq!.Seat);
        Assert.Equal(1, forceReq.Iscall);
    }

    private TKMobileAckMsg CreateLordAck(Action<LordAckMsg> configure)
    {
        var lordAck = new LordAckMsg
        {
            Matchid = MatchId,
        };
        configure(lordAck);

        return new TKMobileAckMsg
        {
            LordAckMsg = lordAck,
        };
    }

    private TKMobileAckMsg CreateAckWithSupportedField(Type ackType)
    {
        return CreateLordAck(lordAck =>
        {
            switch (ackType.Name)
            {
                case nameof(LordWaitClientReadyAck):
                    lordAck.LordwaitclientreadyAckMsg = new LordWaitClientReadyAck();
                    break;
                case nameof(LordGameStartAck):
                    lordAck.LordgamestartAckMsg = new LordGameStartAck();
                    break;
                case nameof(LordInitCardAck):
                    lordAck.LordinitcardAckMsg = new LordInitCardAck
                    {
                        Firstcallseat = 1,
                        Cards = new byte[] { 1 },
                    };
                    break;
                case nameof(LordCallScoreAck):
                    lordAck.LordcallscoreAckMsg = new LordCallScoreAck
                    {
                        Curcallseat = 1,
                        Nextcallseat = 2,
                        Curscore = 0,
                        Validatescore = 1,
                    };
                    break;
                case nameof(LordInitBottomCardAck):
                    lordAck.LordinitbottomcardAckMsg = new LordInitBottomCardAck
                    {
                        Lordseat = 1,
                        Cards = new byte[] { 1 },
                    };
                    break;
                case nameof(LordOperateStartAck):
                    lordAck.LordoperatestartAckMsg = new LordOperateStartAck();
                    break;
                case nameof(LordKickAck):
                    lordAck.LordkickAckMsg = new LordKickAck
                    {
                        Seat = 1,
                        Kick = false,
                    };
                    break;
                case nameof(LordOperateResultAck):
                    lordAck.LordoperateresultAckMsg = new LordOperateResultAck();
                    break;
                case nameof(LordResultAck):
                    lordAck.LordresultAckMsg = new LordResultAck
                    {
                        Winseat = 1,
                    };
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(ackType), ackType, "Unsupported ack type.");
            }
        });
    }
}
