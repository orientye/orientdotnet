using LordUnion.IntegrationTests.Bots;

using LordUnion.IntegrationTests.GameVariants;



namespace CRPC.Tests.LordUnion;



public class MinimalLandlordBotTests

{

    private const uint MatchId = 900001;

    private const uint Seat = 1;



    private readonly ClassicLordVariant _variant = new();



    [Fact]

    public void DecideReady_AlwaysReturnsReadyDecision()

    {

        var bot = new MinimalLandlordBot();



        var decision = bot.DecideReady();



        Assert.Equal(BotDecisionKind.Ready, decision.Kind);

        Assert.NotNull(decision.ToRequest(_variant, MatchId, Seat).LordReqMsg?.LordclientreadyReqMsg);

    }



    [Fact]

    public void DecideBid_PassesWhenCurrentScoreAlreadySet()

    {

        var bot = new MinimalLandlordBot();

        var context = new BidContext(MatchId, CurCallSeat: 1, NextCallSeat: 2, CurScore: 1, ValidateScore: 1);



        var decision = bot.DecideBid(context);



        Assert.Equal(BotDecisionKind.Bid, decision.Kind);

        Assert.Equal(0u, decision.CurScore);



        var request = decision.ToRequest(_variant, MatchId, Seat);

        var bidReq = request.LordReqMsg!.LordcallscoreReqMsg;

        Assert.NotNull(bidReq);

        Assert.Equal(0u, bidReq!.Curscore);

    }



    [Fact]

    public void DecideBid_BidsOneWhenNoScoreCalledYet()

    {

        var bot = new MinimalLandlordBot();

        var context = new BidContext(MatchId, CurCallSeat: 1, NextCallSeat: 2, CurScore: 0, ValidateScore: 0);



        var decision = bot.DecideBid(context);



        Assert.Equal(1u, decision.CurScore);

        Assert.Equal(1u, decision.ToRequest(_variant, MatchId, Seat).LordReqMsg!.LordcallscoreReqMsg!.Curscore);

    }



    [Fact]

    public void DecideBid_PassesAfterPriorBidEvenWhenCurScoreIsZero()

    {

        var bot = new MinimalLandlordBot();

        var context = new BidContext(MatchId, CurCallSeat: 2, NextCallSeat: 2, CurScore: 0, ValidateScore: 1);



        var decision = bot.DecideBid(context);



        Assert.Equal(0u, decision.CurScore);

    }



    [Fact]

    public void DecidePlay_Farmer_AlwaysPasses()

    {

        var bot = CreateBotWithHand(lordSeat: 2, 0, 1, 2);



        var decision = bot.DecidePlay(new PlayContext(MatchId, Seat, NextPlayer: 2, PassPlayer: (int)Seat));



        Assert.Equal(BotDecisionKind.Pass, decision.Kind);

        Assert.Empty(decision.ToRequest(_variant, MatchId, Seat).LordReqMsg!.LordtakeoutcardReqMsg!.Cards);

    }



    [Fact]

    public void DecidePlay_Leading_PlaysSmallestSingle()

    {

        var bot = CreateBotWithHand(lordSeat: Seat, 5, 0, 13); // 6♦, 3♦, 3♣



        var decision = bot.DecidePlay(new PlayContext(MatchId, Seat, NextPlayer: 2, PassPlayer: 0));



        Assert.Equal(BotDecisionKind.Play, decision.Kind);

        Assert.Equal(new byte[] { 0 }, decision.Cards);

    }



    [Fact]

    public void DecidePlay_LeadingSecondAttempt_PlaysSmallestPairWhenAvailable()

    {

        var bot = CreateBotWithHand(lordSeat: Seat, 0, 13, 2);

        bot.State.LandlordLeadAttempt = 1;



        var decision = bot.DecidePlay(new PlayContext(MatchId, Seat, NextPlayer: 2, PassPlayer: 0));



        Assert.Equal(BotDecisionKind.Play, decision.Kind);

        Assert.Equal(new byte[] { 0, 13 }, decision.Cards);

    }



    [Fact]

    public void DecidePlay_LeadingThirdAttempt_PlaysSmallestTripleWhenAvailable()

    {

        var bot = CreateBotWithHand(lordSeat: Seat, 0, 13, 26);

        bot.State.LandlordLeadAttempt = 2;



        var decision = bot.DecidePlay(new PlayContext(MatchId, Seat, NextPlayer: 2, PassPlayer: 0));



        Assert.Equal(BotDecisionKind.Play, decision.Kind);

        Assert.Equal(new byte[] { 0, 13, 26 }, decision.Cards);

    }



    [Fact]

    public void DecidePlay_LeadingFourthAttempt_PlaysSmallestBombWhenAvailable()

    {

        var bot = CreateBotWithHand(lordSeat: Seat, 0, 13, 26, 39);

        bot.State.LandlordLeadAttempt = 3;



        var decision = bot.DecidePlay(new PlayContext(MatchId, Seat, NextPlayer: 2, PassPlayer: 0));



        Assert.Equal(BotDecisionKind.Play, decision.Kind);

        Assert.Equal(new byte[] { 0, 13, 26, 39 }, decision.Cards);

    }



    [Fact]

    public void DecidePlay_FollowingSingle_PlaysSmallestBeatingCard()

    {

        var bot = CreateBotWithHand(lordSeat: Seat, 0, 1, 2); // 3♦, 4♦, 5♦



        var decision = bot.DecidePlay(new PlayContext(

            MatchId,

            Seat,

            NextPlayer: 2,

            PassPlayer: 0,

            LastPlayedCards: new byte[] { 0 }));



        Assert.Equal(BotDecisionKind.Play, decision.Kind);

        Assert.Equal(new byte[] { 1 }, decision.Cards);

    }



    [Fact]

    public void DecidePlay_FollowingSingle_PassesWhenCannotBeat()

    {

        var bot = CreateBotWithHand(lordSeat: Seat, 0, 1); // 3♦, 4♦



        var decision = bot.DecidePlay(new PlayContext(

            MatchId,

            Seat,

            NextPlayer: 2,

            PassPlayer: (int)Seat,

            LastPlayedCards: new byte[] { 12 })); // 2♦



        Assert.Equal(BotDecisionKind.Pass, decision.Kind);

        Assert.Empty(decision.ToRequest(_variant, MatchId, Seat).LordReqMsg!.LordtakeoutcardReqMsg!.Cards);

    }



    [Fact]

    public void DecidePlay_FollowingPair_PlaysSmallestBeatingPair()

    {

        // Hand: pair of 3s (0,13), pair of 5s (2,15)

        var bot = CreateBotWithHand(lordSeat: Seat, 0, 13, 2, 15);



        var decision = bot.DecidePlay(new PlayContext(

            MatchId,

            Seat,

            NextPlayer: 2,

            PassPlayer: 0,

            LastPlayedCards: new byte[] { 1, 1 })); // pair of 4♦



        Assert.Equal(BotDecisionKind.Play, decision.Kind);

        Assert.Equal(new byte[] { 2, 15 }, decision.Cards);

    }



    [Fact]

    public void DecidePlay_UnsupportedShape_Passes()

    {

        var bot = CreateBotWithHand(lordSeat: Seat, 0, 1, 2, 3);



        var decision = bot.DecidePlay(new PlayContext(

            MatchId,

            Seat,

            NextPlayer: 2,

            PassPlayer: (int)Seat,

            LastPlayedCards: new byte[] { 0, 1, 2 }));



        Assert.Equal(BotDecisionKind.Pass, decision.Kind);

    }



    [Fact]

    public void ApplyGameEvent_OwnPlay_RemovesCardsFromHand()

    {

        var bot = CreateBotWithHand(lordSeat: Seat, 0, 1, 2);

        bot.SetSeat(Seat);



        bot.ApplyGameEvent(new GameEvent

        {

            Kind = GameEventKind.CardsPlayed,

            MatchId = MatchId,

            Seat = Seat,

            Cards = new byte[] { 1 },

        });



        Assert.Equal(2, bot.State.Hand.Count);

        Assert.DoesNotContain(bot.State.Hand, c => c.Byte == 1);

        Assert.Equal(new byte[] { 0, 2 }, CardCodec.Encode(bot.State.Hand));

    }



    [Fact]

    public void ApplyGameEvent_CardsDealt_LoadsSortedHand()

    {

        var bot = new MinimalLandlordBot();



        bot.ApplyGameEvent(new GameEvent

        {

            Kind = GameEventKind.CardsDealt,

            MatchId = MatchId,

            Cards = new byte[] { 12, 0, 1 },

        });



        Assert.Equal(new byte[] { 0, 1, 12 }, CardCodec.Encode(bot.State.Hand));

    }



    [Fact]

    public void ApplyGameEvent_TurnStarted_ClearsLastPlayedCards()

    {

        var bot = new MinimalLandlordBot();

        bot.State.LastPlayedCards = new byte[] { 5 };



        bot.ApplyGameEvent(new GameEvent

        {

            Kind = GameEventKind.TurnStarted,

            MatchId = MatchId,

        });



        Assert.Null(bot.State.LastPlayedCards);

    }

    [Fact]
    public void ApplyGameEvent_PassPlayedToLandlord_ClearsLastPlayedCards()
    {
        var bot = CreateBotWithHand(lordSeat: Seat, 0, 1, 2);
        bot.State.LastPlayedCards = new byte[] { 0 };

        bot.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.PassPlayed,
            MatchId = MatchId,
            NextPlayer = Seat,
        });

        Assert.Null(bot.State.LastPlayedCards);
    }

    [Fact]
    public void ApplyGameEvent_PassPlayedToNonLandlord_KeepsLastPlayedCards()
    {
        var bot = CreateBotWithHand(lordSeat: Seat, 0, 1, 2);
        bot.State.LastPlayedCards = new byte[] { 0 };

        bot.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.PassPlayed,
            MatchId = MatchId,
            NextPlayer = 2,
        });

        Assert.Equal(new byte[] { 0 }, bot.State.LastPlayedCards);
    }

    [Fact]
    public void DecidePlay_AfterFarmersPass_LeadsInsteadOfPassing()
    {
        var bot = CreateBotWithHand(lordSeat: Seat, 0, 1, 2);
        bot.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.CardsPlayed,
            MatchId = MatchId,
            Seat = Seat,
            Cards = new byte[] { 0 },
        });
        bot.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.PassPlayed,
            MatchId = MatchId,
            NextPlayer = Seat,
        });

        var decision = bot.DecidePlay(new PlayContext(
            MatchId,
            Seat,
            NextPlayer: 2,
            PassPlayer: 0,
            LastPlayedCards: null));

        Assert.Equal(BotDecisionKind.Play, decision.Kind);
        Assert.Equal(new byte[] { 1 }, decision.Cards);
    }



    private static MinimalLandlordBot CreateBotWithHand(byte[] bytes, uint lordSeat)

    {

        var bot = new MinimalLandlordBot();

        bot.SetSeat(Seat);

        bot.ApplyGameEvent(new GameEvent

        {

            Kind = GameEventKind.CardsDealt,

            MatchId = MatchId,

            Cards = bytes,

        });

        bot.ApplyGameEvent(new GameEvent

        {

            Kind = GameEventKind.LandlordDeclared,

            MatchId = MatchId,

            LordSeat = lordSeat,

        });

        return bot;

    }



    private static MinimalLandlordBot CreateBotWithHand(uint lordSeat, params byte[] bytes) =>

        CreateBotWithHand(bytes, lordSeat);

}


