using System.Text.RegularExpressions;
using LordUnion.IntegrationTests.Bots;
using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Games.TKLord.Replay;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;

namespace LordUnion.IntegrationTests.Tests;

/// <summary>
/// Replays Origin document-order semantic actions (id=2/10) through three
/// <see cref="XmlReplayBotPolicy"/> instances and reports the first decision mismatch.
/// </summary>
public sealed class XmlReplayOriginWalkTests
{
    private static readonly Regex ActionTagPattern = new(
        @"<a\b[^>]*/?>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string FixturePath(string stem) =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "Games",
            "TKLord",
            "Cases",
            $"{stem}.xml"));

    public static IEnumerable<object[]> AllCaseFixtures =>
        Directory.GetFiles(
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Games", "TKLord", "Cases")),
                "*.xml")
            .Select(path => new object[] { Path.GetFileNameWithoutExtension(path)! });

    [Theory]
    [MemberData(nameof(AllCaseFixtures))]
    public void OriginWalk_ManualActions_UntilFirstAuto_PolicyMatchesOrigin(string fixtureStem)
    {
        var path = FixturePath(fixtureStem);
        var script = XmlRecordParser.ParseFile(path);
        var origin = XmlOriginSemanticActions.Parse(path);

        var result = OriginReplayWalker.Walk(script, origin, manualOnly: true);
        Assert.True(result.Success, result.FormatFailure());
    }

    [Theory]
    [MemberData(nameof(AllCaseFixtures))]
    public void OriginWalk_AtFirstAutoAction_PolicyIncorrectlySends(string fixtureStem)
    {
        var path = FixturePath(fixtureStem);
        var script = XmlRecordParser.ParseFile(path);
        var origin = XmlOriginSemanticActions.Parse(path);

        var result = OriginReplayWalker.Walk(script, origin, manualOnly: false);
        Assert.False(result.Success, "expected spurious send before first auto=1 action");
        Assert.Equal("auto-spurious-send", result.Phase);
    }

    [Fact]
    public void OriginWalk_6975304876129_Semantic15_Seat2PlaysPairTrain()
    {
        const string stem = "20260601_7646426975304876129";
        var path = FixturePath(stem);
        var script = XmlRecordParser.ParseFile(path);
        var origin = XmlOriginSemanticActions.Parse(path);

        var result = OriginReplayWalker.WalkUntilSemanticIndex(script, origin, targetIndex: 15);
        Assert.True(result.Success, result.FormatFailure());
        Assert.Equal("SJCJDTCTC9D9", result.Expected);
    }

    internal sealed record OriginSemanticAction(int Id, uint Seat, string O, bool Auto);

    internal sealed record OriginParseResult(uint LordSeat, uint FirstCallSeat, IReadOnlyList<OriginSemanticAction> Actions);

    internal static class XmlOriginSemanticActions
    {
        private static readonly Regex IdPattern = new(@"\bid=""(\d+)""", RegexOptions.Compiled);
        private static readonly Regex SeatPattern = new(@"\bs=""(\d+)""", RegexOptions.Compiled);
        private static readonly Regex ValuePattern = new(@"\bo=""([^""]*)""", RegexOptions.Compiled);
        private static readonly Regex AutoPattern = new(@"\bauto=""(\d+)""", RegexOptions.Compiled);

        public static OriginParseResult Parse(string path)
        {
            var xml = File.ReadAllText(path);
            uint lordSeat = 0;
            var actions = new List<OriginSemanticAction>();

            foreach (Match tagMatch in ActionTagPattern.Matches(xml))
            {
                var tag = tagMatch.Value;
                var idMatch = IdPattern.Match(tag);
                if (!idMatch.Success)
                {
                    continue;
                }

                var id = int.Parse(idMatch.Groups[1].Value);
                if (id == 4)
                {
                    var seatMatch = SeatPattern.Match(tag);
                    if (seatMatch.Success)
                    {
                        lordSeat = uint.Parse(seatMatch.Groups[1].Value);
                    }

                    continue;
                }

                if (id != 2 && id != 10)
                {
                    continue;
                }

                var seat = SeatPattern.Match(tag);
                if (!seat.Success)
                {
                    continue;
                }

                var o = ValuePattern.Match(tag).Success ? ValuePattern.Match(tag).Groups[1].Value : string.Empty;
                var auto = AutoPattern.Match(tag).Success && AutoPattern.Match(tag).Groups[1].Value == "1";
                actions.Add(new OriginSemanticAction(id, uint.Parse(seat.Groups[1].Value), o, auto));
            }

            var firstCallSeat = actions.FirstOrDefault(a => a.Id == 2)?.Seat ?? 0;
            return new OriginParseResult(lordSeat, firstCallSeat, actions);
        }
    }

    internal sealed class OriginWalkResult
    {
        public bool Success { get; init; }

        public int SemanticIndex { get; init; }

        public string? FixtureId { get; init; }

        public string? Phase { get; init; }

        public uint? Seat { get; init; }

        public string? Expected { get; init; }

        public string? Actual { get; init; }

        public string? Detail { get; init; }

        public string FormatFailure() =>
            $"{FixtureId} failed at semantic[{SemanticIndex}] phase={Phase} seat={Seat}: "
            + $"expected={Expected ?? "(null)"} actual={Actual ?? "(null)"}"
            + (Detail is null ? string.Empty : $" ({Detail})");
    }

    internal static class OriginReplayWalker
    {
        private const uint MatchId = 1;

        public static OriginWalkResult WalkUntilSemanticIndex(
            XmlReplayScript script,
            OriginParseResult origin,
            int targetIndex)
        {
            var full = Walk(script, origin, manualOnly: true);
            if (!full.Success)
            {
                return full;
            }

            if (full.SemanticIndex <= targetIndex)
            {
                return new OriginWalkResult
                {
                    Success = false,
                    SemanticIndex = full.SemanticIndex,
                    FixtureId = script.TestRecordId,
                    Phase = "target-index",
                    Expected = $"reach {targetIndex}",
                    Actual = $"reached {full.SemanticIndex}",
                };
            }

            var action = origin.Actions.Where(a => a.Id == 2 || a.Id == 10).ElementAt(targetIndex);
            return new OriginWalkResult
            {
                Success = true,
                SemanticIndex = targetIndex,
                FixtureId = script.TestRecordId,
                Expected = action.O,
            };
        }

        public static OriginWalkResult Walk(
            XmlReplayScript script,
            OriginParseResult origin,
            bool manualOnly)
        {
            var policies = new XmlReplayBotPolicy[3];
            for (uint seat = 0; seat < 3; seat++)
            {
                policies[seat] = XmlReplayBotPolicy.CreateReplay(script, seat);
                policies[seat].SetSeat(seat);
            }

            var dummyMessage = new ProtocolMessage
            {
                Kind = ProtocolMessageKind.LordAck,
                Acknowledgement = new TKMobileAckMsg
                {
                    LordAckMsg = new LordAckMsg { Matchid = MatchId, TimeStamp = 1000 },
                },
            };

            foreach (var policy in policies)
            {
                policy.ApplyGameEvent(new GameEvent
                {
                    Kind = GameEventKind.CardsDealt,
                    MatchId = MatchId,
                    FirstCallSeat = origin.FirstCallSeat,
                    TestRecordId = script.TestRecordId,
                });
            }

            foreach (var policy in policies)
            {
                policy.ApplyGameEvent(new GameEvent
                {
                    Kind = GameEventKind.LandlordDeclared,
                    MatchId = MatchId,
                    LordSeat = origin.LordSeat,
                    Cards = Array.Empty<byte>(),
                });
            }

            uint msgCnt = 0;
            uint lastNonPassSeat = origin.LordSeat;
            GameEvent? triggerEvent = null;
            var semanticIndex = 0;
            var playStarted = false;

            for (var i = 0; i < origin.Actions.Count; i++)
            {
                var action = origin.Actions[i];
                if (action.Id == 2)
                {
                    var policy = policies[action.Seat];
                    var decision = policy.TryDecide(new BotActionContext(
                        new GameEvent
                        {
                            Kind = GameEventKind.BidRequested,
                            MatchId = MatchId,
                            CurCallSeat = action.Seat,
                            NextCallSeat = action.Seat,
                            CurScore = 0,
                            ValidateScore = 0,
                        },
                        dummyMessage,
                        MatchId,
                        action.Seat));

                    if (decision is null || decision.Kind != BotDecisionKind.Bid
                        || decision.CurScore?.ToString() != action.O)
                    {
                        return Fail(script.TestRecordId, semanticIndex, "bid", action.Seat, action.O,
                            decision?.CurScore?.ToString() ?? decision?.Kind.ToString() ?? "null");
                    }

                    semanticIndex++;
                    continue;
                }

                if (manualOnly && action.Auto)
                {
                    return new OriginWalkResult
                    {
                        Success = true,
                        SemanticIndex = semanticIndex,
                        FixtureId = script.TestRecordId,
                        Phase = "manual-until-auto",
                    };
                }

                var actor = action.Seat;
                var policyForActor = policies[actor];

                if (!playStarted)
                {
                    playStarted = true;
                    triggerEvent = new GameEvent
                    {
                        Kind = GameEventKind.OperateFinished,
                        MatchId = MatchId,
                        OperateTypes = new List<uint> { 1 },
                    };
                }
                else if (triggerEvent is null)
                {
                    return Fail(script.TestRecordId, semanticIndex, "trigger", actor, action.O, null,
                        "missing trigger ack");
                }

                if (!action.Auto)
                {
                    var decisionPlay = policyForActor.TryDecide(new BotActionContext(
                        triggerEvent, dummyMessage, MatchId, actor));

                    if (decisionPlay is null)
                    {
                        return Fail(script.TestRecordId, semanticIndex, "play", actor, action.O, "null",
                            $"trigger={triggerEvent.Kind} next={triggerEvent.NextPlayer}");
                    }

                    var expectedPass = string.IsNullOrEmpty(action.O);
                    if (expectedPass)
                    {
                        if (decisionPlay.Kind != BotDecisionKind.Pass)
                        {
                            return Fail(script.TestRecordId, semanticIndex, "play", actor, "<pass>",
                                decisionPlay.Kind.ToString());
                        }
                    }
                    else
                    {
                        if (decisionPlay.Kind != BotDecisionKind.Play)
                        {
                            return Fail(script.TestRecordId, semanticIndex, "play", actor, action.O,
                                decisionPlay.Kind.ToString());
                        }

                        var expectedCards = XmlCardCodec.DecodePlayString(action.O);
                        if (!decisionPlay.Cards.AsSpan().SequenceEqual(expectedCards))
                        {
                            return Fail(
                                script.TestRecordId,
                                semanticIndex,
                                "play-cards",
                                actor,
                                action.O,
                                FormatCards(decisionPlay.Cards ?? Array.Empty<byte>()),
                                $"playIndex mismatch for seat {actor}");
                        }
                    }
                }
                else
                {
                    var spurious = policyForActor.TryDecide(new BotActionContext(
                        triggerEvent, dummyMessage, MatchId, actor));
                    if (spurious is not null)
                    {
                        return Fail(
                            script.TestRecordId,
                            semanticIndex,
                            "auto-spurious-send",
                            actor,
                            action.O,
                            spurious.Kind.ToString(),
                            "policy sent on auto=1 action; live would double-record vs assist");
                    }
                }

                msgCnt++;
                var nextSeat = i + 1 < origin.Actions.Count && origin.Actions[i + 1].Id == 10
                    ? origin.Actions[i + 1].Seat
                    : (actor + 1) % 3;

                GameEvent ack;
                if (string.IsNullOrEmpty(action.O))
                {
                    ack = new GameEvent
                    {
                        Kind = GameEventKind.PassPlayed,
                        MatchId = MatchId,
                        Seat = lastNonPassSeat,
                        PassPlayer = (int)actor,
                        NextPlayer = nextSeat,
                        Cards = Array.Empty<byte>(),
                        TakeoutMsgCnt = msgCnt,
                    };
                }
                else
                {
                    lastNonPassSeat = actor;
                    ack = new GameEvent
                    {
                        Kind = GameEventKind.CardsPlayed,
                        MatchId = MatchId,
                        Seat = actor,
                        NextPlayer = nextSeat,
                        Cards = XmlCardCodec.DecodePlayString(action.O),
                        TakeoutMsgCnt = msgCnt,
                    };
                }

                foreach (var policy in policies)
                {
                    policy.ApplyGameEvent(ack);
                }

                triggerEvent = ack;
                semanticIndex++;
            }

            return new OriginWalkResult { Success = true, SemanticIndex = semanticIndex, FixtureId = script.TestRecordId };
        }

        private static OriginWalkResult Fail(
            string fixtureId,
            int semanticIndex,
            string phase,
            uint seat,
            string expected,
            string? actual,
            string? detail = null) =>
            new()
            {
                Success = false,
                SemanticIndex = semanticIndex,
                FixtureId = fixtureId,
                Phase = phase,
                Seat = seat,
                Expected = expected,
                Actual = actual,
                Detail = detail,
            };

        private static string FormatCards(byte[] cards) =>
            string.Join(",", cards);
    }
}
