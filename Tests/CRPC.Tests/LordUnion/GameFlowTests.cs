using CRpc.Async;
using LordUnion.IntegrationTests.Bots;
using LordUnion.IntegrationTests.Flows;
using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Sessions;
using ProtoBuf;

namespace CRPC.Tests.LordUnion;

public class GameFlowTests : CrpcTestBase
{
    private readonly ServerProtocolCodec codec = new();
    private readonly ClassicLordVariant variant = new();

    private const uint MatchId = 900001;
    private const uint SeatOrder = 1;

    [Fact]
    public void RunUntilFinishedAsync_AutoRespondsAndCompletesOnGameFinished()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var bot = new MinimalLandlordBot();
        var transport = new FakeGameServerTransport();
        transport.BindIncomingHandler(session, codec);

        var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.InGame);
            session.UserId = 214291552;
            session.MatchId = MatchId;
            session.SeatOrder = SeatOrder;

            var flow = new GameFlow(codec);
            var flowTask = flow.RunUntilFinishedAsync(
                session,
                bot,
                variant,
                TimeSpan.FromSeconds(5),
                transport);

            DeliverLordEvent(session, CreateReadyRequestedAck());
            await CRpcTask.Delay(1, loop);
            Assert.Contains(
                session.SentMessages,
                entry => entry.Kind == ProtocolMessageKind.LordReq);

            DeliverLordEvent(session, CreateCardsDealtAck(new byte[] { 0x03, 0x13, 0x23 }));
            await CRpcTask.Delay(1, loop);
            Assert.Contains(
                session.SentMessages,
                entry => entry.Kind == ProtocolMessageKind.LordReq);

            DeliverLordEvent(session, CreateBidRequestedAck(SeatOrder));
            await CRpcTask.Delay(1, loop);

            DeliverLordEvent(session, CreateGameFinishedAck(winSeat: SeatOrder, scores: new[] { 10, -5, -5 }));

            return await flowTask;
        });

        Assert.True(result.Success);
        Assert.Equal(SeatOrder, result.WinSeat);
        Assert.Equal(new[] { 10, -5, -5 }, result.Scores);
        Assert.Equal(AccountSessionState.Finished, session.State);
    }

    [Fact]
    public void RunUntilFinishedAsync_CompletesOnOverGameAck()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var bot = new MinimalLandlordBot();
        var transport = new FakeGameServerTransport();
        transport.BindIncomingHandler(session, codec);

        var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.InGame);
            session.MatchId = MatchId;
            session.SeatOrder = SeatOrder;

            var flow = new GameFlow(codec);
            var flowTask = flow.RunUntilFinishedAsync(
                session,
                bot,
                variant,
                TimeSpan.FromSeconds(5),
                transport);

            DeliverLordEvent(session, CreateOverGameAck());

            return await flowTask;
        });

        Assert.True(result.Success);
        Assert.Equal(AccountSessionState.Finished, session.State);
    }

    [Fact]
    public void RunUntilFinishedAsync_CompletesOnHandOverAck()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var bot = new MinimalLandlordBot();

        var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.InGame);
            session.MatchId = MatchId;
            session.SeatOrder = SeatOrder;

            var flow = new GameFlow(codec);
            var flowTask = flow.RunUntilFinishedAsync(
                session,
                bot,
                variant,
                TimeSpan.FromSeconds(5));

            DeliverLordEvent(session, CreateHandOverAck());

            return await flowTask;
        });

        Assert.True(result.Success);
        Assert.Equal(AccountSessionState.Finished, session.State);
    }

    [Fact]
    public void TryDecodePacket_IdentifiesOverGameAck()
    {
        using var bodyStream = new MemoryStream();
        Serializer.Serialize(bodyStream, new TKMobileAckMsg
        {
            MatchAckMsg = new MatchAckMsg
            {
                Matchid = MatchId,
                OvergameAckMsg = new OverGameAck(),
            },
        });
        var packet = ServerPacketFrame.EncodeFrame(1001, bodyStream.ToArray());

        Assert.True(codec.TryDecodePacket(
            packet,
            new ProtocolDecodeContext(),
            out var message,
            out var error));

        Assert.Null(error);
        Assert.NotNull(message);
        Assert.Equal(ProtocolMessageKind.OverGameAck, message!.Kind);
        Assert.NotNull(message.OverGameAcknowledgement);
        Assert.True(message.HasGameFinishedSignal);
    }

    [Fact]
    public void RunUntilFinishedAsync_TimesOutWhenGameFinishedNeverArrives()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var bot = new MinimalLandlordBot();

        var exception = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.InGame);
            session.MatchId = MatchId;
            session.SeatOrder = SeatOrder;

            var flow = new GameFlow(codec);
            try
            {
                await flow.RunUntilFinishedAsync(
                    session,
                    bot,
                    variant,
                    TimeSpan.FromMilliseconds(50));
                throw new InvalidOperationException("Expected timeout.");
            }
            catch (TimeoutException timeoutException)
            {
                return timeoutException;
            }
        });

        Assert.Contains("GameFinished", exception.Message, StringComparison.Ordinal);
        Assert.Equal(AccountSessionState.Failed, session.State);
    }

    private static void DeliverLordEvent(AccountSession session, ProtocolMessage message)
    {
        session.DeliverIncomingMessage(message);
    }

    private static ProtocolMessage CreateReadyRequestedAck()
    {
        return CreateLordAckMessage(lordAck =>
        {
            lordAck.LordwaitclientreadyAckMsg = new LordWaitClientReadyAck
            {
                Timestamp = 1,
            };
        });
    }

    private static ProtocolMessage CreateCardsDealtAck(byte[] cards)
    {
        return CreateLordAckMessage(lordAck =>
        {
            lordAck.LordinitcardAckMsg = new LordInitCardAck
            {
                Firstcallseat = SeatOrder,
                Cards = cards,
            };
        });
    }

    private static ProtocolMessage CreateBidRequestedAck(uint nextCallSeat)
    {
        return CreateLordAckMessage(lordAck =>
        {
            lordAck.LordcallscoreAckMsg = new LordCallScoreAck
            {
                Curcallseat = nextCallSeat == 0 ? 2u : nextCallSeat - 1,
                Nextcallseat = nextCallSeat,
                Curscore = 0,
                Validatescore = 1,
            };
        });
    }

    private static ProtocolMessage CreateGameFinishedAck(uint winSeat, int[] scores)
    {
        return CreateLordAckMessage(lordAck =>
        {
            var resultAck = new LordResultAck
            {
                Winseat = winSeat,
            };
            resultAck.Score.AddRange(scores);
            lordAck.LordresultAckMsg = resultAck;
        });
    }

    private static ProtocolMessage CreateHandOverAck()
    {
        return new ProtocolMessage
        {
            Header0 = 4001,
            Kind = ProtocolMessageKind.HandOverAck,
            Acknowledgement = new TKMobileAckMsg
            {
                MatchAckMsg = new MatchAckMsg
                {
                    Matchid = MatchId,
                    HandoverAckMsg = new HandOverAck(),
                },
            },
        };
    }

    private static ProtocolMessage CreateOverGameAck()
    {
        return new ProtocolMessage
        {
            Header0 = 4001,
            Kind = ProtocolMessageKind.OverGameAck,
            Acknowledgement = new TKMobileAckMsg
            {
                MatchAckMsg = new MatchAckMsg
                {
                    Matchid = MatchId,
                    OvergameAckMsg = new OverGameAck(),
                },
            },
        };
    }

    private static ProtocolMessage CreateLordAckMessage(Action<LordAckMsg> configure)
    {
        var lordAck = new LordAckMsg
        {
            Matchid = MatchId,
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
