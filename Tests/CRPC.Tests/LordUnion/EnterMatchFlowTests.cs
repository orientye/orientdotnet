using System.Text;
using CRpc.Async;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Flows;
using LordUnion.IntegrationTests.Scenarios;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Sessions;

namespace CRPC.Tests.LordUnion;

public class EnterMatchFlowTests : CrpcTestBase
{
    private readonly ServerProtocolCodec codec = new();

    [Fact]
    public void RunAsync_SucceedsWhenMatchStartAndEnterAcksArrive()
    {
        const uint userId = 214291552;
        const uint matchId = 900001;
        const uint tourneyId = 159740;
        const uint matchPoint = 2008280;
        const uint gameId = 1001;
        const string ticket = "test-ticket";
        const uint seatOrder = 2;

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);

        var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.SignedUp);
            session.UserId = userId;

            var transport = CreateEnterMatchAutoResponder(
                session,
                CreateEnterMatchAck(matchId, tourneyId),
                CreateEnterRoundAck(userId, seatOrder),
                CreateInitGameTableAck(
                    (1, 214291551),
                    (2, userId),
                    (3, 214291553)));

            var flow = new EnterMatchFlow(codec);
            var flowTask = flow.RunAsync(
                session,
                CreateMatch(),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                transport);
            transport.DeliverIncomingMessage(
                CreateStartGameClientAck(userId, matchId, tourneyId, matchPoint, gameId, ticket));
            return await flowTask;
        });

        Assert.True(result.Success);
        Assert.Equal(matchId, result.MatchId);
        Assert.Equal(tourneyId, result.TourneyId);
        Assert.Equal(matchPoint, result.MatchPoint);
        Assert.Equal(gameId, result.GameId);
        Assert.Equal(seatOrder, result.SeatOrder);
        Assert.Equal(userId, result.UserId);
        Assert.Equal(Encoding.UTF8.GetBytes(ticket), result.Ticket);
        Assert.Equal(matchId, result.TableId);
        Assert.Equal(3, result.SeatUserMapping.Count);
        Assert.Equal(userId, result.SeatUserMapping[seatOrder]);
        Assert.Equal(AccountSessionState.InGame, session.State);
        Assert.Equal(matchId, session.MatchId);
        Assert.Equal(seatOrder, session.SeatOrder);
        Assert.Equal(2, transportSentRequestCount(session));
    }

    [Fact]
    public void RunAsync_SucceedsWhenStartClientExAckArrives()
    {
        const uint userId = 214291552;
        const uint matchId = 900002;
        const uint tourneyId = 159740;
        const uint matchPoint = 2008280;
        const uint gameId = 1001;
        var ticket = Encoding.UTF8.GetBytes("binary-ticket");
        const uint seatOrder = 1;

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);

        var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.SignedUp);
            session.UserId = userId;

            var transport = CreateEnterMatchAutoResponder(
                session,
                CreateEnterMatchAck(matchId, tourneyId),
                CreateEnterRoundAck(userId, seatOrder),
                CreateInitGameTableAck((1, userId), (2, 214291552), (3, 214291553)));

            var flow = new EnterMatchFlow(codec);
            var flowTask = flow.RunAsync(
                session,
                CreateMatch(),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                transport);
            transport.DeliverIncomingMessage(
                CreateStartClientExAck(userId, matchId, tourneyId, matchPoint, gameId, ticket));
            return await flowTask;
        });

        Assert.True(result.Success);
        Assert.Equal(matchId, result.MatchId);
        Assert.Equal(ticket, result.Ticket);
        Assert.Equal(seatOrder, result.SeatOrder);
    }

    [Fact]
    public void WaitForMatchStartAsync_SucceedsWhenMatchStartCapturedBeforeWait()
    {
        const uint userId = 214291552;
        const uint matchId = 900010;
        const uint tourneyId = 159740;
        const uint matchPoint = 2008280;
        const uint gameId = 1001;
        var ticket = Encoding.UTF8.GetBytes("burst-ticket");

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var state = new EnterMatchFlowSessionState();

        var matchStart = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.SignedUp);
            session.UserId = userId;

            var transport = new FakeGameServerTransport();
            transport.BindIncomingHandler(session, codec);

            state.CaptureMatchProgressMessage(
                CreateStartClientExAck(userId, matchId, tourneyId, matchPoint, gameId, ticket));

            var flow = new EnterMatchFlow(codec);
            return await flow.WaitForMatchStartAsync(
                session,
                TimeSpan.FromSeconds(5),
                transport,
                state);
        });

        Assert.Equal(matchId, matchStart.MatchId);
        Assert.Equal(tourneyId, matchStart.TourneyId);
        Assert.Equal(matchPoint, matchStart.MatchPoint);
        Assert.Equal(gameId, matchStart.GameId);
        Assert.Equal(ticket, matchStart.Ticket);
    }

    [Fact]
    public void WaitForMatchStartAsync_SucceedsWhenStartClientExAckBurstArrivesViaPushBeforeWaitRegisters()
    {
        const uint userId = 214291552;
        const uint matchId = 900011;
        const uint tourneyId = 159740;
        const uint matchPoint = 2008280;
        const uint gameId = 1001;
        var ticket = Encoding.UTF8.GetBytes("burst-ticket");

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var state = new EnterMatchFlowSessionState();

        var matchStart = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.SignedUp);
            session.UserId = userId;

            var transport = new FakeGameServerTransport();
            transport.BindIncomingHandler(session, codec);
            session.PushMessageReceived = _ => { };

            var flow = new EnterMatchFlow(codec);
            var waitTask = flow.WaitForMatchStartAsync(
                session,
                TimeSpan.FromSeconds(5),
                transport,
                state);

            loop.Post(() =>
            {
                transport.DeliverIncomingMessage(
                    CreateStartClientExAck(userId, matchId, tourneyId, matchPoint, gameId, ticket));
            });

            return await waitTask;
        });

        Assert.Equal(matchId, matchStart.MatchId);
        Assert.Equal(tourneyId, matchStart.TourneyId);
        Assert.Equal(matchPoint, matchStart.MatchPoint);
        Assert.Equal(gameId, matchStart.GameId);
        Assert.Equal(ticket, matchStart.Ticket);
        Assert.NotNull(state.CapturedMatchStartMessage);
    }

    [Fact]
    public void WaitForMatchStartAsync_SucceedsWhenStartClientExCapturedOnPushBeforeWait()
    {
        const uint userId = 214291552;
        const uint matchId = 900012;
        const uint tourneyId = 159740;
        const uint matchPoint = 2008280;
        const uint gameId = 1001;
        var ticket = Encoding.UTF8.GetBytes("early-capture-ticket");

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var state = new EnterMatchFlowSessionState();

        var matchStart = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.SignedUp);
            session.UserId = userId;

            var transport = new FakeGameServerTransport();
            transport.BindIncomingHandler(session, codec);
            EnterMatchFlow.InstallMatchProgressCapture(session, state);

            loop.Post(() =>
            {
                transport.DeliverIncomingMessage(
                    CreateStartClientExAck(userId, matchId, tourneyId, matchPoint, gameId, ticket));
            });

            var flow = new EnterMatchFlow(codec);
            return await flow.WaitForMatchStartAsync(
                session,
                TimeSpan.FromSeconds(5),
                transport,
                state);
        });

        Assert.Equal(matchId, matchStart.MatchId);
        Assert.Equal(tourneyId, matchStart.TourneyId);
        Assert.NotNull(state.CapturedMatchStartMessage);
    }

    [Fact]
    public void RunAsync_TimesOutWhenStartGameClientAckMissing()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);

        var exception = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.SignedUp);
            session.UserId = 12345;

            var transport = new FakeGameServerTransport();
            transport.BindIncomingHandler(session, codec);

            var flow = new EnterMatchFlow(codec);
            try
            {
                await flow.RunAsync(
                    session,
                    CreateMatch(),
                    TimeSpan.FromMilliseconds(50),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(5),
                    transport);
                throw new InvalidOperationException("Expected timeout.");
            }
            catch (TimeoutException timeoutException)
            {
                return timeoutException;
            }
        });

        Assert.Contains("StartGameClientAck or StartClientExAck", exception.Message, StringComparison.Ordinal);
        Assert.Equal(AccountSessionState.Failed, session.State);
    }

    [Fact]
    public void RunAsync_SucceedsWhenEnterRoundAckSeatOrderIsZeroAndInitGameTableProvidesSeat()
    {
        const uint userId = 214291552;
        const uint matchId = 900003;
        const uint tourneyId = 159740;
        const uint matchPoint = 2008280;
        const uint gameId = 1001;
        const string ticket = "test-ticket";
        const uint seatOrder = 1;

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);

        var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.SignedUp);
            session.UserId = userId;

            var transport = CreateEnterMatchAutoResponder(
                session,
                CreateEnterMatchAck(matchId, tourneyId),
                CreateEnterRoundAck(userId, seatOrder: 0),
                CreateInitGameTableAck(
                    (0, 214291551),
                    (1, userId),
                    (2, 214291553)));

            var flow = new EnterMatchFlow(codec);
            var flowTask = flow.RunAsync(
                session,
                CreateMatch(),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                transport);
            transport.DeliverIncomingMessage(
                CreateStartGameClientAck(userId, matchId, tourneyId, matchPoint, gameId, ticket));
            return await flowTask;
        });

        Assert.True(result.Success);
        Assert.Equal(seatOrder, result.SeatOrder);
        Assert.Equal(seatOrder, session.SeatOrder);
    }

    [Fact]
    public void EnterRoundOnlyAsync_SucceedsWhenLordWaitClientReadyArrivesAfterInitGameTable()
    {
        const uint userId = 214291556;
        const uint matchId = 475051244;
        const uint gameId = 1001;
        const uint seatOrder = 2;

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player3", codec);
        var state = new EnterMatchFlowSessionState();
        var matchStart = new EnterMatchStartInfo
        {
            MatchId = matchId,
            GameId = gameId,
            TourneyId = 159740,
            MatchPoint = 2008280,
            Ticket = Encoding.UTF8.GetBytes("ticket"),
        };

        var seatOrderResult = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.EnteringMatch);
            session.UserId = userId;
            session.MatchId = matchId;

            var transport = new FakeGameServerTransport();
            transport.BindIncomingHandler(session, codec);
            session.PushMessageReceived = message => state.CaptureFromAnyMessage(message);
            transport.OnPacketSentAsync = async (packet, packetLoop) =>
            {
                var decoded = transport.DecodeSentPacket(
                    packet,
                    new ProtocolDecodeContext { AccountAlias = session.Alias, Phase = session.CurrentPhase });

                if (decoded.Kind == ProtocolMessageKind.EnterRoundReq)
                {
                    packetLoop.Post(() =>
                    {
                        transport.DeliverIncomingMessage(CreateEnterRoundAck(userId, seatOrder: 0));
                        transport.DeliverIncomingMessage(CreateAddGamePlayerInfoAck(
                            seatOrder,
                            userId));
                        transport.DeliverIncomingMessage(CreateInitGameTableAck(
                            (0, 214291552),
                            (1, 214291554),
                            (2, userId)));
                        transport.DeliverIncomingMessage(CreateLordWaitClientReadyAck(matchId));
                    });
                }

                await CRpcTask.CompletedTask(packetLoop);
            };

            var flow = new EnterMatchFlow(codec);
            return await flow.EnterRoundOnlyAsync(
                session,
                matchStart,
                TimeSpan.FromSeconds(5),
                transport,
                state);
        });

        Assert.Equal(seatOrder, seatOrderResult);
        Assert.Equal(seatOrder, session.SeatOrder);
        Assert.NotNull(state.InitGameTableAck);
    }

    [Fact]
    public void EnterRoundOnlyAsync_SucceedsWhenAddGamePlayerInfoUsesUserId64Only()
    {
        const uint userId = 214291556;
        const uint matchId = 475051244;
        const uint gameId = 1001;
        const uint seatOrder = 2;

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player3", codec);
        var state = new EnterMatchFlowSessionState();
        var matchStart = new EnterMatchStartInfo
        {
            MatchId = matchId,
            GameId = gameId,
            TourneyId = 159740,
            MatchPoint = 2008280,
            Ticket = Encoding.UTF8.GetBytes("ticket"),
        };

        var seatOrderResult = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.EnteringMatch);
            session.UserId = userId;
            session.MatchId = matchId;

            var transport = new FakeGameServerTransport();
            transport.BindIncomingHandler(session, codec);
            session.PushMessageReceived = message => state.CaptureFromAnyMessage(message);
            transport.OnPacketSentAsync = async (packet, packetLoop) =>
            {
                var decoded = transport.DecodeSentPacket(
                    packet,
                    new ProtocolDecodeContext { AccountAlias = session.Alias, Phase = session.CurrentPhase });

                if (decoded.Kind == ProtocolMessageKind.EnterRoundReq)
                {
                    packetLoop.Post(() =>
                    {
                        transport.DeliverIncomingMessage(CreateEnterRoundAck(userId, seatOrder: 0));
                        transport.DeliverIncomingMessage(CreateAddGamePlayerInfoAck(
                            seatOrder,
                            userId: 0,
                            userId64: userId));
                        transport.DeliverIncomingMessage(CreateLordWaitClientReadyAck(matchId));
                    });
                }

                await CRpcTask.CompletedTask(packetLoop);
            };

            var flow = new EnterMatchFlow(codec);
            return await flow.EnterRoundOnlyAsync(
                session,
                matchStart,
                TimeSpan.FromSeconds(5),
                transport,
                state);
        });

        Assert.Equal(seatOrder, seatOrderResult);
        Assert.Equal(seatOrder, session.SeatOrder);
    }

    [Fact]
    public void EnterRoundOnlyAsync_SucceedsWhenAddGamePlayerInfoSeatOrderIsZero()
    {
        const uint userId = 214291556;
        const uint matchId = 475051244;
        const uint gameId = 1001;
        const uint seatOrder = 0;

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player3", codec);
        var state = new EnterMatchFlowSessionState();
        var matchStart = new EnterMatchStartInfo
        {
            MatchId = matchId,
            GameId = gameId,
            TourneyId = 159740,
            MatchPoint = 2008280,
            Ticket = Encoding.UTF8.GetBytes("ticket"),
        };

        var seatOrderResult = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.EnteringMatch);
            session.UserId = userId;
            session.MatchId = matchId;

            var transport = new FakeGameServerTransport();
            transport.BindIncomingHandler(session, codec);
            session.PushMessageReceived = message => state.CaptureFromAnyMessage(message);
            transport.OnPacketSentAsync = async (packet, packetLoop) =>
            {
                var decoded = transport.DecodeSentPacket(
                    packet,
                    new ProtocolDecodeContext { AccountAlias = session.Alias, Phase = session.CurrentPhase });

                if (decoded.Kind == ProtocolMessageKind.EnterRoundReq)
                {
                    packetLoop.Post(() =>
                    {
                        transport.DeliverIncomingMessage(CreateEnterRoundAck(userId, seatOrder: 0));
                        transport.DeliverIncomingMessage(CreateAddGamePlayerInfoAck(
                            seatOrder,
                            userId,
                            userId64: 0));
                    });
                }

                await CRpcTask.CompletedTask(packetLoop);
            };

            var flow = new EnterMatchFlow(codec);
            return await flow.EnterRoundOnlyAsync(
                session,
                matchStart,
                TimeSpan.FromSeconds(5),
                transport,
                state);
        });

        Assert.Equal(seatOrder, seatOrderResult);
        Assert.Equal(seatOrder, session.SeatOrder);
    }

    [Fact]
    public void EnterRoundOnlyAsync_SucceedsWhenEnterRoundBurstArrivesViaPushBeforeWaitRegisters()
    {
        const uint userId = 214291556;
        const uint matchId = 475051244;
        const uint gameId = 1001;
        const uint seatOrder = 2;

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player3", codec);
        var state = new EnterMatchFlowSessionState();
        var matchStart = new EnterMatchStartInfo
        {
            MatchId = matchId,
            GameId = gameId,
            TourneyId = 159740,
            MatchPoint = 2008280,
            Ticket = Encoding.UTF8.GetBytes("ticket"),
        };

        var seatOrderResult = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.EnteringMatch);
            session.UserId = userId;
            session.MatchId = matchId;

            var transport = new FakeGameServerTransport();
            transport.BindIncomingHandler(session, codec);
            session.PushMessageReceived = message => state.CaptureFromAnyMessage(message);

            var flow = new EnterMatchFlow(codec);
            var enterRoundTask = flow.EnterRoundOnlyAsync(
                session,
                matchStart,
                TimeSpan.FromSeconds(5),
                transport,
                state);

            loop.Post(() =>
            {
                transport.DeliverIncomingMessage(CreateEnterRoundAck(userId, seatOrder: 0));
                transport.DeliverIncomingMessage(CreateAddGamePlayerInfoAck(
                    seatOrder,
                    userId: 0,
                    userId64: userId));
            });

            return await enterRoundTask;
        });

        Assert.Equal(seatOrder, seatOrderResult);
        Assert.Equal(seatOrder, session.SeatOrder);
    }

    [Fact]
    public void RunAsync_SucceedsWhenEnterRoundAckArrivesInsteadOfEnterMatchAck()
    {
        const uint userId = 214291552;
        const uint matchId = 900011;
        const uint tourneyId = 159740;
        const uint matchPoint = 2008280;
        const uint gameId = 1001;
        const string ticket = "test-ticket";
        const uint seatOrder = 1;

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);

        var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.SignedUp);
            session.UserId = userId;

            var transport = CreateEnterMatchAutoResponder(
                session,
                enterMatchAck: null,
                CreateEnterRoundAck(userId, seatOrder),
                CreateInitGameTableAck((0, 214291551), (1, userId), (2, 214291553)));

            var flow = new EnterMatchFlow(codec);
            var flowTask = flow.RunAsync(
                session,
                CreateMatch(),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                transport);
            transport.DeliverIncomingMessage(
                CreateStartGameClientAck(userId, matchId, tourneyId, matchPoint, gameId, ticket));
            return await flowTask;
        });

        Assert.True(result.Success);
        Assert.Equal(matchId, result.MatchId);
        Assert.Equal(seatOrder, result.SeatOrder);
        Assert.Equal(AccountSessionState.InGame, session.State);
    }

    [Fact]
    public void RunAsync_TimesOutWhenEnterMatchAckMissing()
    {
        const uint userId = 214291552;
        const uint matchId = 900001;

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);

        var exception = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.SignedUp);
            session.UserId = userId;

            var transport = CreateEnterMatchAutoResponder(
                session,
                enterMatchAck: null,
                enterRoundAck: null,
                initGameTableAck: null);

            var flow = new EnterMatchFlow(codec);
            try
            {
                var flowTask = flow.RunAsync(
                    session,
                    CreateMatch(),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromMilliseconds(50),
                    TimeSpan.FromSeconds(5),
                    transport);
                transport.DeliverIncomingMessage(
                    CreateStartGameClientAck(userId, matchId, tourneyId: 159740, matchPoint: 2008280, gameId: 1001, ticket: "abc"));
                await flowTask;
                throw new InvalidOperationException("Expected timeout.");
            }
            catch (TimeoutException timeoutException)
            {
                return timeoutException;
            }
        });

        Assert.Contains("EnterMatchAck", exception.Message, StringComparison.Ordinal);
        Assert.Equal(AccountSessionState.Failed, session.State);
    }

    [Fact]
    public void Verify_SucceedsWhenThreePlayersShareMatchAndSeats()
    {
        const uint matchId = 900001;

        var results = new[]
        {
            CreateSuccessfulResult(matchId, userId: 101, seatOrder: 0),
            CreateSuccessfulResult(matchId, userId: 102, seatOrder: 1),
            CreateSuccessfulResult(matchId, userId: 103, seatOrder: 2),
        };

        SameTableVerifier.Verify(results);
    }

    [Fact]
    public void Verify_ThrowsWhenMatchIdsDiffer()
    {
        var results = new[]
        {
            CreateSuccessfulResult(matchId: 900001, userId: 101, seatOrder: 0),
            CreateSuccessfulResult(matchId: 900002, userId: 102, seatOrder: 1),
            CreateSuccessfulResult(matchId: 900001, userId: 103, seatOrder: 2),
        };

        var exception = Assert.Throws<InvalidOperationException>(() => SameTableVerifier.Verify(results));
        Assert.Contains("same match", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_ThrowsWhenSeatOrdersDuplicate()
    {
        const uint matchId = 900001;

        var results = new[]
        {
            CreateSuccessfulResult(matchId, userId: 101, seatOrder: 0),
            CreateSuccessfulResult(matchId, userId: 102, seatOrder: 1),
            CreateSuccessfulResult(matchId, userId: 103, seatOrder: 1),
        };

        var exception = Assert.Throws<InvalidOperationException>(() => SameTableVerifier.Verify(results));
        Assert.Contains("Duplicate seat", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static int transportSentRequestCount(AccountSession session)
    {
        return session.SentMessages.Count(message =>
            message.Kind is ProtocolMessageKind.EnterMatchReq or ProtocolMessageKind.EnterRoundReq);
    }

    private static FakeGameServerTransport CreateEnterMatchAutoResponder(
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

    private static MatchConfig CreateMatch() =>
        new() { GameId = 1001, ProductId = 2008280, TourneyId = 159740 };

    private static EnterTableStageResult CreateSuccessfulResult(uint matchId, uint userId, uint seatOrder) =>
        new(
            userId,
            matchId,
            matchId,
            seatOrder,
            new Dictionary<uint, uint>
            {
                [0] = 101,
                [1] = 102,
                [2] = 103,
            });

    private static ProtocolMessage CreateStartClientExAck(
        uint userId,
        uint matchId,
        uint tourneyId,
        uint productId,
        uint gameId,
        byte[] ticket)
    {
        return new ProtocolMessage
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
                        Productid = 2008280,
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

    private static ProtocolMessage CreateLordWaitClientReadyAck(uint matchId)
    {
        return new ProtocolMessage
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
    }

    private static ProtocolMessage CreateAddGamePlayerInfoAck(uint seatOrder, uint userId, ulong userId64 = 0)
    {
        return new ProtocolMessage
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
