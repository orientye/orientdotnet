using System.Text;
using CRpc.Async;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Flows;
using LordUnion.IntegrationTests.Scenarios;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Sessions;

namespace LordUnion.IntegrationTests.Tests;

public class EnterMatchFlowTests : CrpcTestBase
{
    private readonly ServerProtocolCodec codec = new();

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
                LordUnionEnterMatchWireFixtures.CreateStartClientExAck(userId, matchId, tourneyId, matchPoint, gameId, ticket));

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
                    LordUnionEnterMatchWireFixtures.CreateStartClientExAck(userId, matchId, tourneyId, matchPoint, gameId, ticket));
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
                    LordUnionEnterMatchWireFixtures.CreateStartClientExAck(userId, matchId, tourneyId, matchPoint, gameId, ticket));
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
                        transport.DeliverIncomingMessage(LordUnionEnterMatchWireFixtures.CreateEnterRoundAck(userId, seatOrder: 0));
                        transport.DeliverIncomingMessage(LordUnionEnterMatchWireFixtures.CreateAddGamePlayerInfoAck(
                            seatOrder,
                            userId));
                        transport.DeliverIncomingMessage(LordUnionEnterMatchWireFixtures.CreateInitGameTableAck(
                            (0, 214291552),
                            (1, 214291554),
                            (2, userId)));
                        transport.DeliverIncomingMessage(LordUnionEnterMatchWireFixtures.CreateLordWaitClientReadyAck(matchId));
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
                        transport.DeliverIncomingMessage(LordUnionEnterMatchWireFixtures.CreateEnterRoundAck(userId, seatOrder: 0));
                        transport.DeliverIncomingMessage(LordUnionEnterMatchWireFixtures.CreateAddGamePlayerInfoAck(
                            seatOrder,
                            userId: 0,
                            userId64: userId));
                        transport.DeliverIncomingMessage(LordUnionEnterMatchWireFixtures.CreateLordWaitClientReadyAck(matchId));
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
                        transport.DeliverIncomingMessage(LordUnionEnterMatchWireFixtures.CreateEnterRoundAck(userId, seatOrder: 0));
                        transport.DeliverIncomingMessage(LordUnionEnterMatchWireFixtures.CreateAddGamePlayerInfoAck(
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
                transport.DeliverIncomingMessage(LordUnionEnterMatchWireFixtures.CreateEnterRoundAck(userId, seatOrder: 0));
                transport.DeliverIncomingMessage(LordUnionEnterMatchWireFixtures.CreateAddGamePlayerInfoAck(
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
}
