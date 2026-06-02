using LordUnion.IntegrationTests.Bots;
using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Games.TKLord.Replay;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;

namespace LordUnion.IntegrationTests.Tests;

public sealed class XmlReplayCoordinatorTests
{
    [Fact]
    public void RegisterInitCard_AllEmpty_StaysInactive()
    {
        var coordinator = new XmlReplayCoordinator(AppContext.BaseDirectory);
        coordinator.RegisterInitCard(0, null);
        coordinator.RegisterInitCard(1, "");
        coordinator.RegisterInitCard(2, "  ");

        Assert.False(coordinator.IsReplayActive);
    }

    [Fact]
    public void RegisterInitCard_Mismatch_Throws()
    {
        var coordinator = new XmlReplayCoordinator(AppContext.BaseDirectory);
        coordinator.RegisterInitCard(0, "20260601_7646425803181457480");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            coordinator.RegisterInitCard(1, "20260601_7646426185232220179"));

        Assert.Contains("testrecordid mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegisterInitCard_FirstNonEmptyId_LoadsCatalogImmediately()
    {
        const string fixtureId = "20260601_7646425803181457480";
        var coordinator = new XmlReplayCoordinator(AppContext.BaseDirectory);
        coordinator.RegisterInitCard(0, fixtureId);

        Assert.True(coordinator.IsReplayActive);
        Assert.NotNull(coordinator.Catalog);
        Assert.Equal(fixtureId, coordinator.TestRecordId);
    }

    [Fact]
    public void RegisterInitCard_LoadsCatalogWhenActive()
    {
        var coordinator = new XmlReplayCoordinator(AppContext.BaseDirectory);
        coordinator.RegisterInitCard(0, "20260601_7646425803181457480");
        coordinator.RegisterInitCard(1, "20260601_7646425803181457480");
        coordinator.RegisterInitCard(2, "20260601_7646425803181457480");

        Assert.True(coordinator.IsReplayActive);
        Assert.NotNull(coordinator.Catalog);
    }
}

public sealed class XmlReplayBotPolicyTests
{
    private static string FixturePath(string stem) =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "Games",
            "TKLord",
            "Cases",
            $"{stem}.xml"));

    [Fact]
    public void TryDecide_BidRequested_ReplaysRecordedBidScore()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
        var policy = XmlReplayBotPolicy.CreateReplay(script, seat: 1);
        policy.SetSeat(1);

        var decision = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.BidRequested,
                MatchId = 1,
                CurCallSeat = 1,
                NextCallSeat = 1,
                CurScore = 0,
                ValidateScore = 0,
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            1));

        Assert.NotNull(decision);
        Assert.Equal(BotDecisionKind.Bid, decision!.Kind);
        Assert.Equal(1u, decision.CurCallSeat);
        Assert.Equal(2u, decision.CurScore);
    }

    [Fact]
    public void ApplyGameEvent_OwnSeatPlayWithoutPriorSend_AdvancesPlayIndex()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
        Assert.True(script.PlaysBySeat[1].Count >= 2);

        var policy = XmlReplayBotPolicy.CreateReplay(script, seat: 1);
        policy.SetSeat(1);

        var skipped = script.PlaysBySeat[1][0];
        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.CardsPlayed,
            MatchId = 1,
            Seat = 1,
            NextPlayer = 2,
            Cards = skipped.IsPass
                ? Array.Empty<byte>()
                : XmlCardCodec.DecodePlayString(skipped.CardString),
        });

        var decision = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.CardsPlayed,
                MatchId = 1,
                Seat = 0,
                NextPlayer = 1,
                Cards = Array.Empty<byte>(),
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            1));

        Assert.NotNull(decision);
        var expected = script.PlaysBySeat[1][1];
        if (expected.IsPass)
        {
            Assert.Equal(BotDecisionKind.Pass, decision!.Kind);
        }
        else
        {
            Assert.Equal(BotDecisionKind.Play, decision!.Kind);
            Assert.Equal(XmlCardCodec.DecodePlayString(expected.CardString), decision.Cards);
        }
    }

    [Fact]
    public void TryDecide_BidRequested_UsesLocalSeatNotAckCurCallSeat()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
        var policy = XmlReplayBotPolicy.CreateReplay(script, seat: 0);
        policy.SetSeat(0);

        var decision = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.BidRequested,
                MatchId = 1,
                CurCallSeat = 1,
                NextCallSeat = 0,
                CurScore = 0,
                ValidateScore = 2,
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            0));

        Assert.NotNull(decision);
        Assert.Equal(BotDecisionKind.Bid, decision!.Kind);
        Assert.Equal(0u, decision.CurCallSeat);
    }

    [Fact]
    public void TryDecide_PlayRequested_ReplaysRecordedCards()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
        var policy = XmlReplayBotPolicy.CreateReplay(script, seat: 0);
        policy.SetSeat(0);

        _ = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.CardsDealt,
                MatchId = 1,
                FirstCallSeat = 1,
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            0));

        var playDecision = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.CardsPlayed,
                MatchId = 1,
                Seat = 2,
                NextPlayer = 0,
                PassPlayer = 2,
                Cards = Array.Empty<byte>(),
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            0));

        Assert.NotNull(playDecision);
        Assert.Equal(BotDecisionKind.Play, playDecision!.Kind);
        Assert.Equal(new byte[] { 0, 39 }, playDecision.Cards);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TryDecide_NextPlayerServerAuto_ReturnsNull(bool nextAutoPass, bool nextAutoGo)
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
        var policy = XmlReplayBotPolicy.CreateReplay(script, seat: 1);
        policy.SetSeat(1);

        var decision = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.PassPlayed,
                MatchId = 1,
                Seat = 0,
                NextPlayer = 1,
                PassPlayer = 0,
                Cards = Array.Empty<byte>(),
                NextAutoPass = nextAutoPass ? true : null,
                NextAutoGo = nextAutoGo ? true : null,
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            1));

        Assert.Null(decision);
    }

    [Fact]
    public void ApplyGameEvent_NextAutoPassThenOwnSeatPass_AdvancesAndReplaysNextPlay()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
        Assert.True(script.PlaysBySeat[1].Count >= 2);

        var policy = XmlReplayBotPolicy.CreateReplay(script, seat: 1);
        policy.SetSeat(1);

        Assert.Null(policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.CardsPlayed,
                MatchId = 1,
                Seat = 0,
                NextPlayer = 1,
                Cards = new byte[] { 1 },
                NextAutoPass = true,
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            1)));

        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.PassPlayed,
            MatchId = 1,
            Seat = 0,
            PassPlayer = 1,
            NextPlayer = 2,
            Cards = Array.Empty<byte>(),
        });

        var decision = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.CardsPlayed,
                MatchId = 1,
                Seat = 2,
                NextPlayer = 1,
                Cards = Array.Empty<byte>(),
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            1));

        Assert.NotNull(decision);
        var expected = script.PlaysBySeat[1][1];
        if (expected.IsPass)
        {
            Assert.Equal(BotDecisionKind.Pass, decision!.Kind);
        }
        else
        {
            Assert.Equal(BotDecisionKind.Play, decision!.Kind);
            Assert.Equal(XmlCardCodec.DecodePlayString(expected.CardString), decision.Cards);
        }
    }

    [Fact]
    public void TryDecide_LandlordDeclaredThenTurnStarted_DoesNotConsumeFirstLead()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
        const uint lordSeat = 1;
        var policy = XmlReplayBotPolicy.CreateReplay(script, seat: lordSeat);
        policy.SetSeat(lordSeat);
        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.LandlordDeclared,
            MatchId = 1,
            LordSeat = lordSeat,
            Cards = Array.Empty<byte>(),
        });

        var onDeclared = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.LandlordDeclared,
                MatchId = 1,
                LordSeat = lordSeat,
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            lordSeat));

        var onOperateStart = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.TurnStarted,
                MatchId = 1,
                OperateTypes = new List<uint> { 1 },
                SeatList = new List<uint>(),
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            lordSeat));

        var secondOperateStart = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.TurnStarted,
                MatchId = 1,
                SeatList = new List<uint> { lordSeat, 2, 0 },
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            lordSeat));

        Assert.Null(onDeclared);
        Assert.Null(onOperateStart);
        Assert.Null(secondOperateStart);
        Assert.False(policy.State.LandlordFirstLeadDone);
    }

    [Fact]
    public void TryDecide_OperateFinishedKick_TriggersFirstLead()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
        const uint lordSeat = 1;
        var policy = XmlReplayBotPolicy.CreateReplay(script, seat: lordSeat);
        policy.SetSeat(lordSeat);
        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.LandlordDeclared,
            MatchId = 1,
            LordSeat = lordSeat,
            Cards = Array.Empty<byte>(),
        });

        Assert.Null(policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.TurnStarted,
                MatchId = 1,
                OperateTypes = new List<uint> { 1 },
                SeatList = new List<uint> { lordSeat, 2, 0 },
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            lordSeat)));

        var onKickFinished = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.OperateFinished,
                MatchId = 1,
                OperateTypes = new List<uint> { 1 },
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            lordSeat));

        Assert.NotNull(onKickFinished);
        var expected = script.PlaysBySeat[lordSeat][0];
        if (expected.IsPass)
        {
            Assert.Equal(BotDecisionKind.Pass, onKickFinished!.Kind);
        }
        else
        {
            Assert.Equal(BotDecisionKind.Play, onKickFinished!.Kind);
            Assert.Equal(XmlCardCodec.DecodePlayString(expected.CardString), onKickFinished.Cards);
        }
    }

    [Fact]
    public void TryDecide_SecondOperateFinishedWhileAwaitingOwnPlayAck_IsBlocked()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
        const uint lordSeat = 1;
        var policy = XmlReplayBotPolicy.CreateReplay(script, seat: lordSeat);
        policy.SetSeat(lordSeat);
        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.LandlordDeclared,
            MatchId = 1,
            LordSeat = lordSeat,
            Cards = Array.Empty<byte>(),
        });

        var firstLead = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.OperateFinished,
                MatchId = 1,
                OperateTypes = new List<uint> { 1 },
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            lordSeat));

        var duplicate = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.OperateFinished,
                MatchId = 1,
                OperateTypes = new List<uint> { 1 },
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            lordSeat));

        Assert.NotNull(firstLead);
        Assert.Null(duplicate);
    }

    [Fact]
    public void TryDecide_AfterOwnSeatAck_AdvancesToNextRecordedPlay()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
        var policy = XmlReplayBotPolicy.CreateReplay(script, seat: 0);
        policy.SetSeat(0);

        var firstDecision = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.CardsPlayed,
                MatchId = 1,
                Seat = 2,
                NextPlayer = 0,
                PassPlayer = 2,
                Cards = Array.Empty<byte>(),
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            0));

        Assert.NotNull(firstDecision);
        Assert.Null(policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.CardsPlayed,
                MatchId = 1,
                Seat = 2,
                NextPlayer = 0,
                Cards = Array.Empty<byte>(),
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            0)));

        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.CardsPlayed,
            MatchId = 1,
            Seat = 0,
            NextPlayer = 1,
            Cards = firstDecision!.Cards ?? Array.Empty<byte>(),
        });

        var secondDecision = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.CardsPlayed,
                MatchId = 1,
                Seat = 1,
                NextPlayer = 0,
                Cards = Array.Empty<byte>(),
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            0));

        Assert.NotNull(secondDecision);
        Assert.Equal(
            XmlCardCodec.DecodePlayString(script.PlaysBySeat[0][1].CardString),
            secondDecision!.Cards);
    }

    [Fact]
    public void Parse_Case6760959164547_LandlordFirstPlay_IsOpeningRun()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646426760959164547"));
        var landlordPlays = script.PlaysBySeat[1];

        Assert.NotEmpty(landlordPlays);
        Assert.False(landlordPlays[0].IsPass);
        Assert.Equal("S9H8C7D6D5S4C3", landlordPlays[0].CardString);
        Assert.Equal(7, XmlCardCodec.DecodePlayString(landlordPlays[0].CardString).Length);
    }

    [Fact]
    public void TryDecide_TakeoutNextPlayerBeforeLandlordFirstLead_NonLordSeatWaits()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646426760959164547"));
        const uint lordSeat = 1;
        var policy = XmlReplayBotPolicy.CreateReplay(script, seat: 2);
        policy.SetSeat(2);
        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.LandlordDeclared,
            MatchId = 1,
            LordSeat = lordSeat,
            Cards = Array.Empty<byte>(),
        });

        var decision = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.CardsPlayed,
                MatchId = 1,
                Seat = lordSeat,
                NextPlayer = 2,
                Cards = Array.Empty<byte>(),
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            2));

        Assert.Null(decision);
        Assert.False(policy.State.LandlordFirstLeadDone);
    }

    [Fact]
    public void TryDecide_TurnStartedNonLordSeat_DoesNotConsumeFirstLead()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646426760959164547"));
        const uint lordSeat = 1;
        var policy = XmlReplayBotPolicy.CreateReplay(script, seat: 2);
        policy.SetSeat(2);
        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.LandlordDeclared,
            MatchId = 1,
            LordSeat = lordSeat,
            Cards = Array.Empty<byte>(),
        });

        var decision = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.TurnStarted,
                MatchId = 1,
                SeatList = new List<uint> { 2, 0, 1 },
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            2));

        Assert.Null(decision);
        Assert.False(policy.State.LandlordFirstLeadDone);
    }

    [Fact]
    public void ApplyGameEvent_PassAck_UsesPassPlayerNotAckSeatToAdvancePlayIndex()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
        const uint lordSeat = 0;
        const uint farmerSeat = 1;
        var policy = XmlReplayBotPolicy.CreateReplay(script, seat: farmerSeat);
        policy.SetSeat(farmerSeat);
        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.LandlordDeclared,
            MatchId = 1,
            LordSeat = lordSeat,
            Cards = Array.Empty<byte>(),
        });
        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.CardsPlayed,
            MatchId = 1,
            Seat = lordSeat,
            NextPlayer = farmerSeat,
            Cards = XmlCardCodec.DecodePlayString("D3S3"),
            TakeoutMsgCnt = 2,
        });

        var firstFollow = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.CardsPlayed,
                MatchId = 1,
                Seat = lordSeat,
                NextPlayer = farmerSeat,
                Cards = XmlCardCodec.DecodePlayString("D3S3"),
                TakeoutMsgCnt = 2,
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            farmerSeat));
        Assert.NotNull(firstFollow);
        Assert.Equal(BotDecisionKind.Play, firstFollow!.Kind);

        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.CardsPlayed,
            MatchId = 1,
            Seat = farmerSeat,
            NextPlayer = 2,
            Cards = XmlCardCodec.DecodePlayString("H4S4"),
            TakeoutMsgCnt = 3,
        });

        var passDecision = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.CardsPlayed,
                MatchId = 1,
                Seat = 2,
                NextPlayer = farmerSeat,
                Cards = new byte[] { 1, 2 },
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1001 },
                },
            },
            1,
            farmerSeat));
        Assert.NotNull(passDecision);
        Assert.Equal(BotDecisionKind.Pass, passDecision!.Kind);

        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.PassPlayed,
            MatchId = 1,
            Seat = lordSeat,
            PassPlayer = (int)farmerSeat,
            NextPlayer = 2,
            Cards = Array.Empty<byte>(),
            TakeoutMsgCnt = 6,
        });

        var afterPass = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.CardsPlayed,
                MatchId = 1,
                Seat = lordSeat,
                NextPlayer = farmerSeat,
                Cards = new byte[] { 1, 2 },
                TakeoutMsgCnt = 6,
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1002 },
                },
            },
            1,
            farmerSeat));

        Assert.NotNull(afterPass);
    }

    [Fact]
    public void TryDecide_FarmerFirstFollow_RequiresLandlordOpeningCardsPlayedAck()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
        const uint lordSeat = 0;
        const uint farmerSeat = 1;
        var policy = XmlReplayBotPolicy.CreateReplay(script, seat: farmerSeat);
        policy.SetSeat(farmerSeat);
        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.LandlordDeclared,
            MatchId = 1,
            LordSeat = lordSeat,
            Cards = Array.Empty<byte>(),
        });

        var tooEarly = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.CardsPlayed,
                MatchId = 1,
                Seat = 2,
                NextPlayer = farmerSeat,
                Cards = new byte[] { 1, 2 },
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            farmerSeat));

        Assert.Null(tooEarly);

        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.CardsPlayed,
            MatchId = 1,
            Seat = lordSeat,
            NextPlayer = farmerSeat,
            Cards = XmlCardCodec.DecodePlayString("D3S3"),
            TakeoutMsgCnt = 5,
        });

        var follow = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.CardsPlayed,
                MatchId = 1,
                Seat = lordSeat,
                NextPlayer = farmerSeat,
                Cards = XmlCardCodec.DecodePlayString("D3S3"),
                TakeoutMsgCnt = 5,
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            farmerSeat));

        Assert.NotNull(follow);
        Assert.Equal(BotDecisionKind.Play, follow!.Kind);
        Assert.Equal(XmlCardCodec.DecodePlayString("H4S4"), follow.Cards);
        Assert.Equal(5u, policy.State.TakeoutMsgCnt);
    }

    [Fact]
    public void TryDecide_SecondFarmerFirstFollow_TriggersAfterUpstreamFarmerPlay()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
        const uint lordSeat = 0;
        const uint firstFarmerSeat = 1;
        const uint secondFarmerSeat = 2;
        var policy = XmlReplayBotPolicy.CreateReplay(script, seat: secondFarmerSeat);
        policy.SetSeat(secondFarmerSeat);
        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.LandlordDeclared,
            MatchId = 1,
            LordSeat = lordSeat,
            Cards = Array.Empty<byte>(),
        });
        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.CardsPlayed,
            MatchId = 1,
            Seat = lordSeat,
            NextPlayer = firstFarmerSeat,
            Cards = XmlCardCodec.DecodePlayString("D3S3"),
            TakeoutMsgCnt = 2,
        });
        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.CardsPlayed,
            MatchId = 1,
            Seat = firstFarmerSeat,
            NextPlayer = secondFarmerSeat,
            Cards = XmlCardCodec.DecodePlayString("H4S4"),
            TakeoutMsgCnt = 3,
        });

        var follow = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.CardsPlayed,
                MatchId = 1,
                Seat = firstFarmerSeat,
                NextPlayer = secondFarmerSeat,
                Cards = XmlCardCodec.DecodePlayString("H4S4"),
                TakeoutMsgCnt = 3,
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            secondFarmerSeat));

        Assert.NotNull(follow);
        Assert.Equal(BotDecisionKind.Play, follow!.Kind);
        Assert.Equal(XmlCardCodec.DecodePlayString("HQCQ"), follow.Cards);
        Assert.True(policy.State.LandlordFirstLeadDone);
    }

    [Fact]
    public void TryDecide_TurnStartedKickSeat_FarmerDeclinesKickOnce()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
        const uint lordSeat = 0;
        const uint farmerSeat = 1;
        var policy = XmlReplayBotPolicy.CreateReplay(script, seat: farmerSeat);
        policy.SetSeat(farmerSeat);
        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.LandlordDeclared,
            MatchId = 1,
            LordSeat = lordSeat,
            Cards = Array.Empty<byte>(),
        });

        var kickContext = new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.TurnStarted,
                MatchId = 1,
                OperateTypes = new List<uint> { 1 },
                SeatList = new List<uint> { farmerSeat, 2 },
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            farmerSeat);

        var first = policy.TryDecide(kickContext);
        var second = policy.TryDecide(kickContext);

        Assert.NotNull(first);
        Assert.Equal(BotDecisionKind.Kick, first!.Kind);
        Assert.False(first.Kick);
        Assert.Null(second);
    }

    [Fact]
    public void TryDecide_TurnStartedKickSeat_LordDoesNotRespond()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
        const uint lordSeat = 0;
        var policy = XmlReplayBotPolicy.CreateReplay(script, seat: lordSeat);
        policy.SetSeat(lordSeat);
        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.LandlordDeclared,
            MatchId = 1,
            LordSeat = lordSeat,
            Cards = Array.Empty<byte>(),
        });

        var decision = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.TurnStarted,
                MatchId = 1,
                OperateTypes = new List<uint> { 1 },
                SeatList = new List<uint> { 1, 2 },
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            lordSeat));

        Assert.Null(decision);
    }

    [Fact]
    public void TryDecide_TakeoutAckNextPlayerLandlordWaitsForOperateFinished_OnlyOneFirstLead()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646426760959164547"));
        const uint lordSeat = 1;
        var policy = XmlReplayBotPolicy.CreateReplay(script, seat: lordSeat);
        policy.SetSeat(lordSeat);
        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.LandlordDeclared,
            MatchId = 1,
            LordSeat = lordSeat,
            Cards = Array.Empty<byte>(),
        });

        var fromTakeoutHint = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.PassPlayed,
                MatchId = 1,
                Seat = 0,
                NextPlayer = lordSeat,
                PassPlayer = 0,
                Cards = Array.Empty<byte>(),
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            lordSeat));

        Assert.Null(fromTakeoutHint);

        var fromKickFinished = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.OperateFinished,
                MatchId = 1,
                OperateTypes = new List<uint> { 1 },
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            lordSeat));

        Assert.NotNull(fromKickFinished);
        Assert.Equal(BotDecisionKind.Play, fromKickFinished!.Kind);
        Assert.Equal(XmlCardCodec.DecodePlayString("S9H8C7D6D5S4C3"), fromKickFinished.Cards);

        Assert.Null(policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.TurnStarted,
                MatchId = 1,
                SeatList = new List<uint> { lordSeat, 2, 0 },
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            lordSeat)));
    }

    [Fact]
    public void TryDecide_LateCatalogActivation_ReplaysSeat0ThirdBidFromFixture()
    {
        const string fixtureId = "20260601_7646425803181457480";
        var coordinator = new XmlReplayCoordinator(AppContext.BaseDirectory);
        var policy = XmlReplayBotPolicy.CreateWithCoordinator(seat: 0, coordinator);
        policy.SetSeat(0);

        policy.ApplyGameEvent(new GameEvent
        {
            Kind = GameEventKind.CardsDealt,
            MatchId = 1,
            FirstCallSeat = 1,
            TestRecordId = fixtureId,
        });
        Assert.True(coordinator.IsReplayActive);

        coordinator.RegisterInitCard(1, fixtureId);
        coordinator.RegisterInitCard(2, fixtureId);
        Assert.True(coordinator.IsReplayActive);

        var decision = policy.TryDecide(new BotActionContext(
            new GameEvent
            {
                Kind = GameEventKind.BidRequested,
                MatchId = 1,
                CurCallSeat = 2,
                NextCallSeat = 0,
                CurScore = 0,
                ValidateScore = 2,
            },
            new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = 1, TimeStamp = 1000 },
                },
            },
            1,
            0));

        Assert.NotNull(decision);
        Assert.Equal(BotDecisionKind.Bid, decision!.Kind);
        Assert.Equal(0u, decision.CurCallSeat);
        Assert.Equal(3u, decision.CurScore);
    }
}
