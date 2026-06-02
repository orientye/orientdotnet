using CRpc.Async;
using LordUnion.IntegrationTests.Bots.Pacing;
using LordUnion.IntegrationTests.Flows;
using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Games.TKLord.Replay;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Sessions;
using ProtoBuf;

namespace LordUnion.IntegrationTests.Tests;

public sealed class XmlReplayFakeGameFlowTests : CrpcTestBase
{
    private readonly ServerProtocolCodec codec = new();
    private readonly ClassicLordVariant variant = new();

    private const uint MatchId = XmlReplayFakeScriptBuilder.DefaultMatchId;
    private const uint SeatOrder = 1;
    private const string FixtureId = XmlReplayFakeScriptBuilder.DefaultFixtureId;

    [Fact]
    public void GameFlow_WithTestRecordId_ReplaysFirstBidFromFixture()
    {
        var coordinator = CreateCoordinatorWithAllSeats(FixtureId);
        var loop = new CRpcLoop();

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            var session = CreateInGameSession(loop);
            var policy = coordinator.CreatePolicy(SeatOrder);
            var flow = new GameFlow(codec);
            var flowTask = flow.RunUntilFinishedAsync(
                session,
                policy,
                variant,
                TimeSpan.FromSeconds(5),
                ImmediateActionScheduler.Instance);

            DeliverLordEvent(session, XmlReplayFakeScriptBuilder.ReadyRequested(MatchId));
            await CRpcTask.Delay(1, loop);
            Assert.Contains(
                session.SentMessages,
                entry => entry.Kind == ProtocolMessageKind.LordReq);

            DeliverLordEvent(
                session,
                XmlReplayFakeScriptBuilder.InitCardWithTestRecordId(
                    FixtureId,
                    firstCallSeat: SeatOrder,
                    matchId: MatchId));
            await CRpcTask.Delay(1, loop);

            var bidReq = FindLastCallScoreReq(session);
            Assert.NotNull(bidReq);
            Assert.Equal(2u, bidReq!.Curscore);

            DeliverLordEvent(
                session,
                XmlReplayFakeScriptBuilder.GameFinished(SeatOrder, [10, -5, -5], MatchId));

            var result = await flowTask;
            Assert.True(result.Success);
            return 0;
        });
    }

    [Fact]
    public void GameFlow_MissingFixture_FailsBeforeBid()
    {
        const string missingId = "missing_case_id_for_replay_test";
        var coordinator = new XmlReplayCoordinator(AppContext.BaseDirectory);

        var ex = Assert.Throws<FileNotFoundException>(() =>
            coordinator.RegisterInitCard(0, missingId));

        Assert.Contains(missingId, ex.Message, StringComparison.Ordinal);
    }

    private static XmlReplayCoordinator CreateCoordinatorWithAllSeats(string testRecordId)
    {
        var coordinator = new XmlReplayCoordinator(AppContext.BaseDirectory);
        coordinator.RegisterInitCard(0, testRecordId);
        coordinator.RegisterInitCard(1, testRecordId);
        coordinator.RegisterInitCard(2, testRecordId);
        return coordinator;
    }

    private AccountSession CreateInGameSession(CRpcLoop loop)
    {
        var session = new AccountSession(loop, "player1", codec);
        session.SetState(AccountSessionState.InGame);
        session.UserId = 214291552;
        session.MatchId = MatchId;
        session.SeatOrder = SeatOrder;
        return session;
    }

    private static void DeliverLordEvent(AccountSession session, ProtocolMessage message) =>
        session.DeliverIncomingMessage(message);

    private static LordCallScoreReq? FindLastCallScoreReq(AccountSession session)
    {
        for (var i = session.SentPackets.Count - 1; i >= 0; i--)
        {
            var packet = session.SentPackets[i];
            if (packet.Length < ServerPacketFrame.HeaderLength)
            {
                continue;
            }

            var frame = ServerPacketFrame.DecodeHeader(packet.AsSpan());
            var bodyLength = frame.BodyLength;
            if (packet.Length < ServerPacketFrame.HeaderLength + bodyLength)
            {
                continue;
            }

            var body = packet.AsMemory(ServerPacketFrame.HeaderLength, bodyLength);
            var request = Serializer.Deserialize<TKMobileReqMsg>(body.Span);
            if (request.LordReqMsg?.LordcallscoreReqMsg is { } bidReq)
            {
                return bidReq;
            }
        }

        return null;
    }
}
