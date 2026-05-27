using CRpc.Async;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Flows;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Sessions;

namespace CRPC.Tests.LordUnion;

public class AccountCleanupFlowTests : CrpcTestBase
{
    private readonly ServerProtocolCodec codec = new();

    [Fact]
    public void RunAsync_SendsUnsignupAndRecordsAck()
    {
        const uint userId = 214291552;
        const uint tourneyId = 159740;
        const uint matchPoint = 2008280;

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);

        var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.LoggedIn);
            session.UserId = userId;
            session.Nickname = "Tester";

            var transport = CreateAutoResponder(
                session,
                CreateUnsignupAck(tourneyId, matchPoint, param: 0));

            var flow = new AccountCleanupFlow(codec);
            return await flow.RunAsync(session, CreateMatch(), transport, drainWindowMs: 0);
        });

        Assert.True(result.UnsignupSent);
        Assert.True(result.UnsignupAckReceived);
        Assert.Equal(0u, result.UnsignupParam);
        Assert.Equal(AccountSessionState.LoggedIn, session.State);
    }

    [Fact]
    public void RunAsync_ExitsDiscoveredMatchAfterUnsignup()
    {
        const uint matchId = 475051244;

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);

        var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.LoggedIn);
            session.UserId = 214291552;

            var transport = CreateAutoResponder(
                session,
                CreateUnsignupAck(tourneyId: 159740, matchPoint: 2008280, param: 0),
                CreateExitGameAck(matchId));

            var flow = new AccountCleanupFlow(codec);
            var flowTask = flow.RunAsync(session, CreateMatch(), transport, drainWindowMs: 100);
            await CRpcTask.Delay(20, loop);
            session.DeliverIncomingMessage(CreateLordMatchPush(matchId));
            return await flowTask;
        });

        Assert.Contains(matchId, result.DiscoveredMatchIds);
        Assert.Contains(matchId, result.ExitGameAttemptedMatchIds);
        Assert.Contains(matchId, result.ExitMatchAttemptedMatchIds);
    }

    [Fact]
    public void CaptureMatchIds_RecordsStartClientExMatchId()
    {
        var discovered = new HashSet<uint>();
        AccountCleanupFlow.CaptureMatchIds(
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.StartClientExAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LobbyAckMsg = new LobbyAckMsg
                    {
                        StartclientexAckMsg = new StartClientExAck
                        {
                            Matchid = 475051244,
                            Tourneyid = 159740,
                            Gameid = 1001,
                            Productid = 2008280,
                        },
                    },
                },
            },
            discovered);

        Assert.Contains(475051244u, discovered);
    }

    private static MatchConfig CreateMatch() =>
        new()
        {
            GameId = 1001,
            ProductId = 2008280,
            TourneyId = 159740,
        };

    private static ProtocolMessage CreateUnsignupAck(uint tourneyId, uint matchPoint, uint param)
    {
        return new ProtocolMessage
        {
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

    private static ProtocolMessage CreateLordMatchPush(uint matchId)
    {
        return new ProtocolMessage
        {
            Kind = ProtocolMessageKind.LordAck,
            Acknowledgement = new TKMobileAckMsg
            {
                LordAckMsg = new LordAckMsg
                {
                    Matchid = matchId,
                },
            },
        };
    }

    private static FakeGameServerTransport CreateAutoResponder(
        AccountSession session,
        params ProtocolMessage[] responses)
    {
        var transport = new FakeGameServerTransport();
        transport.BindIncomingHandler(session, new ServerProtocolCodec());
        var index = 0;
        transport.OnPacketSentAsync = async (packet, loop) =>
        {
            _ = packet;
            if (index < responses.Length)
            {
                transport.DeliverIncomingMessage(responses[index++]);
            }

            await CRpcTask.CompletedTask(loop);
        };
        return transport;
    }
}
