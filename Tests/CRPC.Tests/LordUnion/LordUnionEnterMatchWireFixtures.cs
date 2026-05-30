using System.Text;
using CRpc.Async;
using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Scenarios;
using LordUnion.IntegrationTests.Sessions;

namespace CRPC.Tests.LordUnion;

internal static class LordUnionEnterMatchWireFixtures
{
    public static LordUnionGameProfile CreateClassicProfile(
        uint gameId = 1001,
        uint tourneyId = 159740,
        uint matchPoint = 2008280) =>
        new()
        {
            ProfileId = "classic",
            GameId = gameId,
            ProductId = matchPoint,
            TourneyId = tourneyId,
            MatchPoint = matchPoint,
            Variant = new ClassicLordVariant(),
        };

    public static int TransportSentEnterRequestCount(AccountSession session) =>
        session.SentMessages.Count(message =>
            message.Kind is ProtocolMessageKind.EnterMatchReq or ProtocolMessageKind.EnterRoundReq);

    public static FakeGameServerTransport CreateEnterMatchAutoResponder(
        AccountSession session,
        ProtocolMessage? enterMatchAck,
        ProtocolMessage? enterRoundAck,
        ProtocolMessage? initGameTableAck)
    {
        var transport = new FakeGameServerTransport();
        transport.BindIncomingHandler(session, new ServerProtocolCodec());

        transport.OnPacketSentAsync = async (packet, packetLoop) =>
        {
            var decoded = transport.DecodeSentPacket(
                packet,
                new ProtocolDecodeContext { AccountAlias = session.Alias, Phase = session.CurrentPhase });

            if (decoded.Kind == ProtocolMessageKind.EnterMatchReq)
            {
                if (enterMatchAck is not null)
                {
                    transport.DeliverIncomingMessage(enterMatchAck);
                }
                else if (enterRoundAck is not null)
                {
                    transport.DeliverIncomingMessage(enterRoundAck);
                }
            }
            else if (decoded.Kind == ProtocolMessageKind.EnterRoundReq)
            {
                if (enterRoundAck is not null)
                {
                    transport.DeliverIncomingMessage(enterRoundAck);
                }

                if (initGameTableAck is not null)
                {
                    transport.DeliverIncomingMessage(initGameTableAck);
                }
            }

            await CRpcTask.CompletedTask(packetLoop);
        };

        return transport;
    }

    public static ProtocolMessage CreateStartClientExAck(
        uint userId,
        uint matchId,
        uint tourneyId,
        uint productId,
        uint gameId,
        byte[] ticket) =>
        new()
        {
            Header0 = 3002,
            Kind = ProtocolMessageKind.StartClientExAck,
            Acknowledgement = new TKMobileAckMsg
            {
                LobbyAckMsg = new LobbyAckMsg
                {
                    StartclientexAckMsg = new StartClientExAck
                    {
                        Userid = userId,
                        Matchid = matchId,
                        Tourneyid = tourneyId,
                        Productid = productId,
                        Gameid = gameId,
                        Ticket = ticket,
                    },
                },
            },
        };

    public static ProtocolMessage CreateStartGameClientAck(
        uint userId,
        uint matchId,
        uint tourneyId,
        uint matchPoint,
        uint gameId,
        string ticket) =>
        new()
        {
            Header0 = 3001,
            Kind = ProtocolMessageKind.StartGameClientAck,
            Acknowledgement = new TKMobileAckMsg
            {
                LobbyAckMsg = new LobbyAckMsg
                {
                    StartgameclientAckMsg = new StartGameClientAck
                    {
                        Userid = userId,
                        Matchid = matchId,
                        Tourneyid = tourneyId,
                        Matchpoint = matchPoint,
                        Gameid = gameId,
                        Productid = 2008280,
                        Ticket = ticket,
                    },
                },
            },
        };

    public static ProtocolMessage CreateEnterMatchAck(uint matchId, uint tourneyId) =>
        new()
        {
            Header0 = 4001,
            Kind = ProtocolMessageKind.EnterMatchAck,
            Acknowledgement = new TKMobileAckMsg
            {
                MatchAckMsg = new MatchAckMsg
                {
                    EntermatchAckMsg = new EnterMatchAck
                    {
                        Matchid = matchId,
                        Tourneyid = tourneyId,
                        Matchname = "test-match",
                        Matchstarttime = 1,
                    },
                },
            },
        };

    public static ProtocolMessage CreateEnterRoundAck(uint userId, uint seatOrder) =>
        new()
        {
            Header0 = 4001,
            Kind = ProtocolMessageKind.EnterRoundAck,
            Acknowledgement = new TKMobileAckMsg
            {
                MatchAckMsg = new MatchAckMsg
                {
                    EnterroundAckMsg = new EnterRoundAck
                    {
                        Userid = userId,
                        Seatorder = seatOrder,
                        Usertype = 1,
                    },
                },
            },
        };

    public static ProtocolMessage CreateAddGamePlayerInfoAck(uint seatOrder, uint userId, ulong userId64 = 0) =>
        new()
        {
            Header0 = 1001,
            Kind = ProtocolMessageKind.AddGamePlayerInfoAck,
            Acknowledgement = new TKMobileAckMsg
            {
                MatchAckMsg = new MatchAckMsg
                {
                    AddgameplayerinfoAckMsg = new AddGamePlayerInfoAck
                    {
                        Playerinfo = new GamePlayerInfo
                        {
                            Seatorder = seatOrder,
                            Userid = userId,
                            Userid64 = userId64,
                            Nickname = $"player-{seatOrder}",
                            Score = 0,
                            Arrived = true,
                            Netstatus = 1,
                        },
                    },
                },
            },
        };

    public static ProtocolMessage CreateLordWaitClientReadyAck(uint matchId) =>
        new()
        {
            Header0 = 1001,
            Kind = ProtocolMessageKind.LordAck,
            Acknowledgement = new TKMobileAckMsg
            {
                LordAckMsg = new LordAckMsg
                {
                    Matchid = matchId,
                    LordwaitclientreadyAckMsg = new LordWaitClientReadyAck
                    {
                        Timestamp = 1,
                    },
                },
            },
        };

    public static ProtocolMessage CreateInitGameTableAck(params (uint seatIndex, uint userId)[] seats)
    {
        var ack = new InitGameTableAck
        {
            Maxaddtohp = 1,
            Minaddtohp = 1,
            Exchangerate = 1,
            Hpmode = 1,
        };

        foreach (var (seatIndex, seatUserId) in seats)
        {
            while (ack.Playerinfolist.Count <= seatIndex)
            {
                ack.Playerinfolist.Add(new SeatPlayerInfo
                {
                    Nickname = $"placeholder-{ack.Playerinfolist.Count}",
                });
            }

            ack.Playerinfolist[(int)seatIndex] = new SeatPlayerInfo
            {
                Userid = seatUserId,
                Nickname = $"player-{seatUserId}",
            };
        }

        return new ProtocolMessage
        {
            Header0 = 4001,
            Kind = ProtocolMessageKind.InitGameTableAck,
            Acknowledgement = new TKMobileAckMsg
            {
                MatchAckMsg = new MatchAckMsg
                {
                    InitgametableAckMsg = ack,
                },
            },
        };
    }
}
