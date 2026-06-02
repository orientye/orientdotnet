using CRpc.Async;
using LordUnion.IntegrationTests.Bots;
using LordUnion.IntegrationTests.Bots.Pacing;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Scenarios;
using LordUnion.IntegrationTests.Sessions;

namespace LordUnion.IntegrationTests.Tests;

public class HumanRandomActionSchedulerTests : CrpcTestBase
{
    [Fact]
    public void GetDelayMilliseconds_ReadyIsImmediate()
    {
        var scheduler = CreateScheduler(seed: 1);
        var delay = scheduler.GetDelayMilliseconds(CreateContext(BotDecisionKind.Ready, "player1"));

        Assert.Equal(0, delay);
    }

    [Fact]
    public void GetDelayMilliseconds_BidStaysWithinConfiguredRange()
    {
        var scheduler = CreateScheduler(seed: 42);
        var delay = scheduler.GetDelayMilliseconds(CreateContext(BotDecisionKind.Bid, "player2"));

        Assert.InRange(delay, 1000, 3000 + 500);
    }

    [Fact]
    public void GetDelayMilliseconds_PlayStaysWithinConfiguredRange()
    {
        var scheduler = CreateScheduler(seed: 42);
        var delay = scheduler.GetDelayMilliseconds(CreateContext(BotDecisionKind.Play, "player3"));

        Assert.InRange(delay, 2000, 6000 + 500);
    }

    [Fact]
    public void GetDelayMilliseconds_DifferentAliasesGetDifferentJitter()
    {
        var scheduler = CreateScheduler(seed: 1);
        var player1 = scheduler.GetDelayMilliseconds(CreateContext(BotDecisionKind.Bid, "player1"));
        var player2 = scheduler.GetDelayMilliseconds(CreateContext(BotDecisionKind.Bid, "player2"));

        Assert.NotEqual(player1, player2);
    }

    [Fact]
    public void ActionSchedulerFactory_SkipBotPacingUsesImmediate()
    {
        var scheduler = ActionSchedulerFactory.Create(
            new BotConfig { Pacing = "human-random" },
            new TimeoutConfig(),
            new ScenarioRunOptions { SkipBotPacing = true });

        Assert.IsType<ImmediateActionScheduler>(scheduler);
    }

    private static HumanRandomActionScheduler CreateScheduler(int seed)
    {
        return new HumanRandomActionScheduler(
            new BotPacingOptions
            {
                BidMinMs = 1000,
                BidMaxMs = 3000,
                PlayMinMs = 2000,
                PlayMaxMs = 6000,
                AliasJitterMaxMs = 500,
            },
            TimeSpan.FromSeconds(30),
            new Random(seed));
    }

    private static BotSendContext CreateContext(BotDecisionKind kind, string alias)
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, alias, new ServerProtocolCodec());
        var decision = kind switch
        {
            BotDecisionKind.Ready => BotDecision.Ready(),
            BotDecisionKind.Bid => BotDecision.Bid(0, 1, 1, 1),
            BotDecisionKind.Play => BotDecision.Play(1, new byte[] { 3 }),
            BotDecisionKind.Pass => BotDecision.Pass(1, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        return new BotSendContext(
            session,
            new GameEvent { Kind = GameEventKind.BidRequested, MatchId = 1 },
            new ProtocolMessage { Kind = ProtocolMessageKind.LordAck },
            decision,
            DateTimeOffset.UtcNow);
    }
}
