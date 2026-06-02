using LordUnion.IntegrationTests.Bots;
using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;

namespace LordUnion.IntegrationTests.Tests;

public class MinimalLandlordBotPolicyTests
{
    private const uint MatchId = 900001;
    private const uint Seat = 1;

    [Fact]
    public void TryDecide_ReturnsReadyOnReadyRequested()
    {
        var policy = new MinimalLandlordBotPolicy();
        policy.SetSeat(Seat);

        var decision = policy.TryDecide(new BotActionContext(
            new GameEvent { Kind = GameEventKind.ReadyRequested, MatchId = MatchId },
            CreateSourceMessage(),
            MatchId,
            Seat));

        Assert.NotNull(decision);
        Assert.Equal(BotDecisionKind.Ready, decision!.Kind);
    }

    [Fact]
    public void TryDecide_ReturnsBidWhenNextCallSeatMatches()
    {
        var policy = new MinimalLandlordBotPolicy();
        policy.SetSeat(Seat);

        var decision = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.BidRequested,
                MatchId = MatchId,
                NextCallSeat = Seat,
                CurScore = 0,
                ValidateScore = 1,
            },
            CreateSourceMessage(),
            MatchId,
            Seat));

        Assert.NotNull(decision);
        Assert.Equal(BotDecisionKind.Bid, decision!.Kind);
        Assert.Equal(0u, decision.CurScore);
    }

    [Fact]
    public void TryDecide_ReturnsNullWhenNotMyTurn()
    {
        var policy = new MinimalLandlordBotPolicy();
        policy.SetSeat(Seat);

        var decision = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.BidRequested,
                MatchId = MatchId,
                NextCallSeat = 2,
            },
            CreateSourceMessage(),
            MatchId,
            Seat));

        Assert.Null(decision);
    }

    [Fact]
    public void TryDecide_ReturnsPlayOnLandlordDeclared_WhenLordSeatMatches()
    {
        var policy = new MinimalLandlordBotPolicy();
        policy.SetSeat(Seat);
        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.CardsDealt,
            MatchId = MatchId,
            Cards = new byte[] { 5, 0, 13 },
        });

        var landlordDeclared = new GameEvent
        {
            Kind = GameEventKind.LandlordDeclared,
            MatchId = MatchId,
            LordSeat = Seat,
            Cards = new byte[] { 1, 2 },
        };
        policy.ApplyGameEvent(landlordDeclared);

        var decision = policy.TryDecide(new BotActionContext(
            landlordDeclared,
            CreateSourceMessage(),
            MatchId,
            Seat));

        Assert.NotNull(decision);
        Assert.Equal(BotDecisionKind.Play, decision!.Kind);
        Assert.Equal(new byte[] { 0 }, decision.Cards);
    }

    [Fact]
    public void TryDecide_ReturnsPlayOnTurnStarted_WhenIsLeadTurn()
    {
        var policy = new MinimalLandlordBotPolicy();
        policy.SetSeat(Seat);
        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.CardsDealt,
            MatchId = MatchId,
            Cards = new byte[] { 5, 0, 13 },
        });
        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.LandlordDeclared,
            MatchId = MatchId,
            LordSeat = Seat,
        });

        var turnStarted = new GameEvent
        {
            Kind = GameEventKind.TurnStarted,
            MatchId = MatchId,
            SeatList = new List<uint> { Seat, 2, 0 },
        };

        var decision = policy.TryDecide(new BotActionContext(
            turnStarted,
            CreateSourceMessage(),
            MatchId,
            Seat));

        Assert.NotNull(decision);
        Assert.Equal(BotDecisionKind.Play, decision!.Kind);
        Assert.Equal(new byte[] { 0 }, decision.Cards);
    }

    private static ProtocolMessage CreateSourceMessage()
    {
        return new ProtocolMessage
        {
            Kind = ProtocolMessageKind.LordAck,
            Acknowledgement = new TKMobileAckMsg
            {
                LordAckMsg = new LordAckMsg { Matchid = MatchId, TimeStamp = 1000 },
            },
        };
    }
}
