using CRpc.Async;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Flows;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Scenarios;
using LordUnion.IntegrationTests.Sessions;

namespace CRPC.Tests.LordUnion;

public class ThreePlayersOneGameScenarioTests : CrpcTestBase
{
    private readonly ServerProtocolCodec codec = new();

    private const uint MatchId = 900001;
    private const uint TourneyId = 159740;
    private const uint MatchPoint = 2008280;
    private const uint GameId = 1001;

    [Fact]
    public void RunAsync_FailsWhenPlayer2LoginFails()
    {
        var config = CreateConfig();
        var options = new ScenarioRunOptions
        {
            TransportFactory = new FakeScenarioTransportFactory(
                CreateLobbyScripts(loginFailureAlias: "player2"),
                codec),
        };

        var loop = new CRpcLoop();
        var report = CRpcLoopRunner.RunUntilComplete(loop, () =>
            new ThreePlayersOneGameScenario(codec).RunAsync(loop, config, options));

        Assert.False(report.Success);
        Assert.NotNull(report.FirstFailure);
        Assert.Equal("player2", report.FirstFailure!.AccountAlias);
        Assert.Contains("error code 99", report.FirstFailure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RunAsync_FailsWhenPlayer2SignupFails()
    {
        var config = CreateConfig();
        var options = new ScenarioRunOptions
        {
            TransportFactory = new FakeScenarioTransportFactory(
                CreateLobbyScripts(signupFailureAlias: "player2"),
                codec),
        };

        var loop = new CRpcLoop();
        var report = CRpcLoopRunner.RunUntilComplete(loop, () =>
            new ThreePlayersOneGameScenario(codec).RunAsync(loop, config, options));

        Assert.False(report.Success);
        Assert.NotNull(report.FirstFailure);
        Assert.Equal("player2", report.FirstFailure!.AccountAlias);
        Assert.Contains("error code 7", report.FirstFailure.Message, StringComparison.Ordinal);
        Assert.All(report.AccountTimings, timing => Assert.Equal(TimeSpan.Zero, timing.EnterMatchDuration));
    }

    [Fact]
    public void RunAsync_SucceedsWhenThreePlayersEnterSameTableAndGameCompletes()
    {
        var config = CreateConfig();
        var options = new ScenarioRunOptions
        {
            TransportFactory = new FakeScenarioTransportFactory(CreateLobbyScripts(), codec),
            SkipBotPacing = true,
            MatchStartAckFactory = session => CreateStartGameClientAck(
                session.UserId ?? 0,
                MatchId,
                TourneyId,
                MatchPoint,
                GameId,
                ticket: $"ticket-{session.Alias}"),
            GameFlowOverride = (session, _, _, _, _) =>
                CRpcTask.FromResult(
                    new GameFlowResult
                    {
                        Success = true,
                        WinSeat = session.SeatOrder,
                        Scores = new[] { 1, -1, 0 },
                    },
                    session.Loop),
        };

        var loop = new CRpcLoop();
        var report = CRpcLoopRunner.RunUntilComplete(loop, () =>
            new ThreePlayersOneGameScenario(codec).RunAsync(loop, config, options));

        Assert.True(report.Success);
        Assert.Null(report.FirstFailure);
        Assert.Equal(MatchId, report.MatchId);
        Assert.Equal(MatchId, report.TableId);
        Assert.Equal(3, report.AccountTimings.Count);
        Assert.Equal(1u, report.WinSeat);
    }

    private static LordUnionTestConfig CreateConfig()
    {
        return new LordUnionTestConfig
        {
            Server = new ServerConfig { Host = "127.0.0.1", Port = 30301 },
            Protocol = new ProtocolConfig { AppId = 2, AnonymousSerialId = 2000, LoginType = 2 },
            Match = new MatchConfig { GameId = GameId, ProductId = MatchPoint, TourneyId = TourneyId },
            Accounts =
            [
                new AccountConfig { Alias = "player1", Username = "TJJ006628", Password = "3YXRQW" },
                new AccountConfig { Alias = "player2", Username = "TJJ006629", Password = "3YRQ83" },
                new AccountConfig { Alias = "player3", Username = "TJJ006630", Password = "Q5EDHU" },
            ],
            Timeouts = new TimeoutConfig
            {
                LoginTimeout = TimeSpan.FromSeconds(5),
                SignupTimeout = TimeSpan.FromSeconds(5),
                MatchStartTimeout = TimeSpan.FromSeconds(5),
                EnterMatchTimeout = TimeSpan.FromSeconds(5),
                EnterRoundTimeout = TimeSpan.FromSeconds(5),
                GameOverTimeout = TimeSpan.FromSeconds(5),
            },
        };
    }

    private Dictionary<string, FakeTransportScript> CreateLobbyScripts(
        string? loginFailureAlias = null,
        string? signupFailureAlias = null)
    {
        var userIds = new Dictionary<string, uint>
        {
            ["player1"] = 214291551,
            ["player2"] = 214291552,
            ["player3"] = 214291553,
        };

        var seatOrders = new Dictionary<string, uint>
        {
            ["player1"] = 0,
            ["player2"] = 1,
            ["player3"] = 2,
        };

        return userIds.ToDictionary(
            entry => entry.Key,
            entry => new FakeTransportScript
            {
                OnPacketSentAsync = (transport, session, packet, loop) =>
                    HandleLobbyPacketAsync(
                        transport,
                        session,
                        packet,
                        loop,
                        entry.Value,
                        seatOrders[entry.Key],
                        loginFailureAlias == entry.Key,
                        signupFailureAlias == entry.Key),
            });
    }

    private async CRpcTask HandleLobbyPacketAsync(
        FakeGameServerTransport transport,
        AccountSession session,
        byte[] packet,
        CRpcLoop loop,
        uint userId,
        uint seatOrder,
        bool failLogin,
        bool failSignup)
    {
        var decoded = transport.DecodeSentPacket(
            packet,
            new ProtocolDecodeContext { AccountAlias = session.Alias, Phase = session.CurrentPhase });

        switch (decoded.Kind)
        {
            case ProtocolMessageKind.AnonymousBrowseReq:
                transport.DeliverIncomingMessage(CreateAnonymousBrowseAck(1000 + seatOrder));
                break;

            case ProtocolMessageKind.CommonLoginReq:
                transport.DeliverIncomingMessage(
                    failLogin
                        ? CreateCommonLoginAck(2000 + seatOrder, param: 99, userId: userId)
                        : CreateCommonLoginAck(2000 + seatOrder, param: 0, userId: userId, nickname: session.Alias));
                break;

            case ProtocolMessageKind.TourneySignupReq:
                transport.DeliverIncomingMessage(
                    CreateSignupAck(
                        TourneyId,
                        MatchPoint,
                        (int)GameId,
                        param: failSignup ? 7u : 0u));
                break;

            case ProtocolMessageKind.TourneyUnsignupReq:
                transport.DeliverIncomingMessage(CreateUnsignupAck(TourneyId, MatchPoint, param: 0));
                break;

            case ProtocolMessageKind.ExitGameReq:
                transport.DeliverIncomingMessage(CreateExitGameAck(MatchId));
                break;

            case ProtocolMessageKind.EnterMatchReq:
                transport.DeliverIncomingMessage(CreateEnterMatchAck(MatchId, TourneyId));
                break;

            case ProtocolMessageKind.EnterRoundReq:
                transport.DeliverIncomingMessage(CreateEnterRoundAck(userId, seatOrder));
                transport.DeliverIncomingMessage(CreateInitGameTableAck(
                    (0, 214291551),
                    (1, 214291552),
                    (2, 214291553)));
                break;
        }

        await CRpcTask.CompletedTask(loop);
    }

    private static ProtocolMessage CreateAnonymousBrowseAck(uint header0, string? aesKey = null)
    {
        return new ProtocolMessage
        {
            Header0 = header0,
            Kind = ProtocolMessageKind.AnonymousBrowseAck,
            Acknowledgement = new TKMobileAckMsg
            {
                LobbyAckMsg = new LobbyAckMsg
                {
                    AnonymousAckMsg = new AnonymousBrowseAck
                    {
                        Anonymousid = 7,
                        U64servertime = 1_779_693_696_815,
                        Param = aesKey ?? "test-aes-key",
                    },
                },
            },
        };
    }

    private static ProtocolMessage CreateCommonLoginAck(
        uint header0,
        uint param,
        uint userId,
        string nickname = "")
    {
        return new ProtocolMessage
        {
            Header0 = header0,
            Kind = ProtocolMessageKind.CommonLoginAck,
            Param = param,
            Acknowledgement = new TKMobileAckMsg
            {
                Param = param,
                LobbyAckMsg = new LobbyAckMsg
                {
                    CommonloginAckMsg = new CommonLoginAck
                    {
                        Userinfo = new LcUserInfoEx { Userid = userId, Nickname = nickname },
                    },
                },
            },
        };
    }

    private static ProtocolMessage CreateSignupAck(uint tourneyId, uint matchPoint, int gameId, uint param)
    {
        return new ProtocolMessage
        {
            Header0 = 3000,
            Kind = ProtocolMessageKind.TourneySignupAck,
            Acknowledgement = new TKMobileAckMsg
            {
                LobbyAckMsg = new LobbyAckMsg
                {
                    TourneysignupexAckMsg = new TourneySignupExAck
                    {
                        Param = param,
                        Tourneyid = tourneyId,
                        Matchpoint = matchPoint,
                        Gameid = gameId,
                    },
                },
            },
        };
    }

    private static ProtocolMessage CreateUnsignupAck(uint tourneyId, uint matchPoint, uint param)
    {
        return new ProtocolMessage
        {
            Header0 = 3000,
            Kind = ProtocolMessageKind.TourneyUnsignupAck,
            Acknowledgement = new TKMobileAckMsg
            {
                LobbyAckMsg = new LobbyAckMsg
                {
                    TourneyunsignupAckMsg = new TourneyUnsignupAck
                    {
                        Tourneyid = tourneyId,
                        Matchpoint = matchPoint,
                        Param = param,
                    },
                },
            },
        };
    }

    private static ProtocolMessage CreateExitGameAck(uint matchId)
    {
        return new ProtocolMessage
        {
            Header0 = 4001,
            Kind = ProtocolMessageKind.ExitGameAck,
            Acknowledgement = new TKMobileAckMsg
            {
                MatchAckMsg = new MatchAckMsg
                {
                    Matchid = matchId,
                    ExitgameAckMsg = new ExitGameAck(),
                },
            },
        };
    }

    private static ProtocolMessage CreateStartGameClientAck(
        uint userId,
        uint matchId,
        uint tourneyId,
        uint matchPoint,
        uint gameId,
        string ticket)
    {
        return new ProtocolMessage
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
                        Productid = MatchPoint,
                        Ticket = ticket,
                    },
                },
            },
        };
    }

    private static ProtocolMessage CreateEnterMatchAck(uint matchId, uint tourneyId)
    {
        return new ProtocolMessage
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
    }

    private static ProtocolMessage CreateEnterRoundAck(uint userId, uint seatOrder)
    {
        return new ProtocolMessage
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
    }

    private static ProtocolMessage CreateInitGameTableAck(params (uint seatIndex, uint userId)[] seats)
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
