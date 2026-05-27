using CRpc.Async;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Flows;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Sessions;

namespace CRPC.Tests.LordUnion;

public class SignupFlowTests : CrpcTestBase
{
    private readonly ServerProtocolCodec codec = new();

    [Fact]
    public void RunAsync_SucceedsWhenSignupAckParamIsZero()
    {
        const uint userId = 214291552;
        const uint tourneyId = 159740;
        const uint matchPoint = 2008280;
        const int gameId = 1001;

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);

        var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.LoggedIn);
            session.UserId = userId;
            session.Nickname = "Tester";

            var transport = CreateAutoResponder(
                session,
                CreateSignupAck(tourneyId, matchPoint, gameId, param: 0));

            var flow = new SignupFlow(codec);
            return await flow.RunAsync(
                session,
                CreateMatch(),
                TimeSpan.FromSeconds(5),
                transport);
        });

        Assert.True(result.Success);
        Assert.Equal(0u, result.SignupErrorCode);
        Assert.Equal(0u, result.MobileAckParam);
        Assert.Equal(tourneyId, result.TourneyId);
        Assert.Equal(matchPoint, result.MatchPoint);
        Assert.Equal(gameId, result.GameId);
        Assert.Equal(AccountSessionState.SignedUp, session.State);
    }

    [Fact]
    public void RunAsync_ThrowsWhenSignupAckParamIsNonZero()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);

        var exception = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.LoggedIn);
            session.UserId = 12345;

            var transport = CreateAutoResponder(
                session,
                CreateSignupAck(tourneyId: 159740, matchPoint: 2008280, gameId: 1001, param: 7));

            var flow = new SignupFlow(codec);
            try
            {
                await flow.RunAsync(
                    session,
                    CreateMatch(),
                    TimeSpan.FromSeconds(5),
                    transport);
                throw new InvalidOperationException("Expected signup failure.");
            }
            catch (InvalidOperationException invalidOperationException)
            {
                return invalidOperationException;
            }
        });

        Assert.Contains("error code 7", exception.Message, StringComparison.Ordinal);
        Assert.Equal(AccountSessionState.Failed, session.State);
    }

    [Fact]
    public void RunAsync_TimesOutWhenSignupAckMissing()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);

        var exception = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.LoggedIn);
            session.UserId = 12345;

            var transport = new FakeGameServerTransport();
            transport.BindIncomingHandler(session, codec);

            var flow = new SignupFlow(codec);
            try
            {
                await flow.RunAsync(
                    session,
                    CreateMatch(),
                    TimeSpan.FromMilliseconds(50),
                    transport);
                throw new InvalidOperationException("Expected timeout.");
            }
            catch (TimeoutException timeoutException)
            {
                return timeoutException;
            }
        });

        Assert.Contains("TourneySignupAck", exception.Message, StringComparison.Ordinal);
        Assert.Equal(AccountSessionState.Failed, session.State);
    }

    [Fact]
    public void RunAsync_ThrowsWhenSessionIsNotLoggedIn()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);

        var exception = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.Connected);
            session.UserId = null;

            var flow = new SignupFlow(codec);
            try
            {
                await flow.RunAsync(
                    session,
                    CreateMatch(),
                    TimeSpan.FromSeconds(5));
                throw new InvalidOperationException("Expected invalid operation.");
            }
            catch (InvalidOperationException invalidOperationException)
            {
                return invalidOperationException;
            }
        });

        Assert.Contains("logged in", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunAsync_ThrowsWhenUserIdMissing()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player2", codec);

        var exception = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.LoggedIn);
            session.UserId = null;

            var flow = new SignupFlow(codec);
            try
            {
                await flow.RunAsync(
                    session,
                    CreateMatch(),
                    TimeSpan.FromSeconds(5));
                throw new InvalidOperationException("Expected invalid operation.");
            }
            catch (InvalidOperationException invalidOperationException)
            {
                return invalidOperationException;
            }
        });

        Assert.Contains("non-zero UserId", exception.Message, StringComparison.Ordinal);
    }

    private static FakeGameServerTransport CreateAutoResponder(
        AccountSession session,
        ProtocolMessage signupAck)
    {
        var transport = new FakeGameServerTransport();
        transport.BindIncomingHandler(session, new ServerProtocolCodec());
        transport.OnPacketSentAsync = async (packet, packetLoop) =>
        {
            var decoded = transport.DecodeSentPacket(
                packet,
                new ProtocolDecodeContext { AccountAlias = session.Alias, Phase = session.CurrentPhase });
            if (decoded.Kind == ProtocolMessageKind.TourneySignupReq)
            {
                transport.DeliverIncomingMessage(signupAck);
            }

            await CRpcTask.CompletedTask(packetLoop);
        };

        return transport;
    }

    [Fact]
    public void RunAsync_CapturesOuterMobileAckParamSeparatelyFromSignupAckParam()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);

        var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.LoggedIn);
            session.UserId = 12345;

            var transport = CreateAutoResponder(
                session,
                CreateSignupAck(tourneyId: 159740, matchPoint: 2008280, gameId: 1001, param: 0, mobileParam: 6));

            var flow = new SignupFlow(codec);
            return await flow.RunAsync(
                session,
                CreateMatch(),
                TimeSpan.FromSeconds(5),
                transport);
        });

        Assert.True(result.Success);
        Assert.Equal(0u, result.SignupErrorCode);
        Assert.Equal(6u, result.MobileAckParam);
    }

    [Fact]
    public void RunAsync_CapturesEmbeddedStartClientExFromCombinedSignupAck()
    {
        const uint userId = 214291552;
        const uint matchId = 475051244;
        const uint tourneyId = 159740;
        const uint matchPoint = 2008280;
        const uint gameId = 1001;
        var ticket = new byte[] { 0x01, 0x02 };

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var matchProgressState = new EnterMatchFlowSessionState();

        var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.LoggedIn);
            session.UserId = userId;

            var transport = CreateAutoResponder(
                session,
                CreateCombinedSignupAndStartClientExAck(
                    userId,
                    matchId,
                    tourneyId,
                    matchPoint,
                    gameId,
                    ticket));

            var flow = new SignupFlow(codec);
            return await flow.RunAsync(
                session,
                CreateMatch(),
                TimeSpan.FromSeconds(5),
                transport,
                matchProgressState);
        });

        Assert.True(result.Success);
        Assert.NotNull(matchProgressState.CapturedMatchStartMessage);
        Assert.Equal(ProtocolMessageKind.StartClientExAck, matchProgressState.CapturedMatchStartMessage!.Kind);
        Assert.Equal(matchId, matchProgressState.CapturedMatchStartMessage.StartClientExAcknowledgement?.Matchid);
    }

    private static MatchConfig CreateMatch() =>
        new() { GameId = 1001, ProductId = 2008280, TourneyId = 159740 };

    private static ProtocolMessage CreateSignupAck(
        uint tourneyId,
        uint matchPoint,
        int gameId,
        uint param,
        uint mobileParam = 0,
        int flags = 0)
    {
        return new ProtocolMessage
        {
            Header0 = 3001,
            Kind = ProtocolMessageKind.TourneySignupAck,
            Param = mobileParam,
            Acknowledgement = new TKMobileAckMsg
            {
                Param = mobileParam,
                LobbyAckMsg = new LobbyAckMsg
                {
                    TourneysignupexAckMsg = new TourneySignupExAck
                    {
                        Tourneyid = tourneyId,
                        Param = param,
                        Matchpoint = matchPoint,
                        Gameid = gameId,
                        Flags = flags,
                    },
                },
            },
        };
    }

    private static ProtocolMessage CreateCombinedSignupAndStartClientExAck(
        uint userId,
        uint matchId,
        uint tourneyId,
        uint matchPoint,
        uint gameId,
        byte[] ticket)
    {
        return new ProtocolMessage
        {
            Header0 = 3001,
            Kind = ProtocolMessageKind.TourneySignupAck,
            Acknowledgement = new TKMobileAckMsg
            {
                LobbyAckMsg = new LobbyAckMsg
                {
                    TourneysignupexAckMsg = new TourneySignupExAck
                    {
                        Tourneyid = tourneyId,
                        Param = 0,
                        Matchpoint = matchPoint,
                        Gameid = (int)gameId,
                    },
                    StartclientexAckMsg = new StartClientExAck
                    {
                        Userid = userId,
                        Tourneyid = tourneyId,
                        Matchid = matchId,
                        Gameid = gameId,
                        Productid = matchPoint,
                        Ticket = ticket,
                    },
                },
            },
        };
    }
}
