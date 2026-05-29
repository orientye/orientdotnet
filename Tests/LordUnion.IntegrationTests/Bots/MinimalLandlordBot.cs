using LordUnion.IntegrationTests.GameVariants;


namespace LordUnion.IntegrationTests.Bots;

public sealed record BidContext(
    uint MatchId,
    uint CurCallSeat,
    uint NextCallSeat,
    uint CurScore,
    uint ValidateScore);

public sealed record PlayContext(
    uint MatchId,
    uint Seat,
    uint NextPlayer,
    int PassPlayer,
    byte[]? LastPlayedCards = null);

/// <summary>
/// Deterministic minimal bot for classic Dou Dizhu integration tests.
/// Farmers always pass; landlord leads minimum single/pair/triple/bomb in rotation.
/// </summary>
public sealed class MinimalLandlordBot

{
    private const byte SmallJoker = 52;

    private const byte BigJoker = 53;


    public BotGameState State { get; } = new();


    public void SetSeat(uint seat) => State.MySeat = seat;


    public void ApplyGameEvent(GameEvent gameEvent)

    {
        switch (gameEvent.Kind)

        {
            case GameEventKind.CardsDealt when gameEvent.Cards is { } dealt:

                State.Hand.Clear();

                State.Hand.AddRange(CardCodec.Decode(dealt));

                State.LastPlayedCards = null;

                State.LordSeat = null;

                State.LandlordFirstLeadDone = false;

                State.LandlordLeadAttempt = 0;

                break;


            case GameEventKind.LandlordDeclared:

                State.LordSeat = gameEvent.LordSeat;

                if (gameEvent.LordSeat == State.MySeat && gameEvent.Cards is { } bottom)

                {
                    State.Hand.AddRange(CardCodec.Decode(bottom));

                    State.Hand.Sort(CardCodec.Compare);
                }


                break;


            case GameEventKind.CardsPlayed when gameEvent.Seat == State.MySeat && gameEvent.Cards is { } played:

                RemoveFromHand(played);

                break;


            case GameEventKind.CardsPlayed when gameEvent.Cards is { Length: > 0 } trickCards:

                State.LastPlayedCards = trickCards.ToArray();

                break;


            case GameEventKind.PassPlayed
                when gameEvent.NextPlayer == State.LordSeat:
                // Both farmers passed; landlord leads a new trick.
                State.LastPlayedCards = null;
                break;

            case GameEventKind.PassPlayed:
                break;


            case GameEventKind.TurnStarted:

                State.LastPlayedCards = null;

                break;
        }
    }


    public BotDecision DecideReady() => BotDecision.Ready();


    public BotDecision DecideBid(BidContext context)

    {
        // ValidateScore is the current table high bid (m_nScore). CurScore is the last action and is 0 on pass.

        // Open at 1 only when nobody has called yet; otherwise pass so we never re-bid an existing score.

        var score = context.ValidateScore == 0 ? 1u : 0u;

        return BotDecision.Bid(context.CurCallSeat, context.NextCallSeat, context.ValidateScore, score);
    }


    public BotDecision DecidePlay(PlayContext context)

    {
        if (State.LordSeat != State.MySeat)

        {
            return BotDecision.Pass(context.NextPlayer, context.PassPlayer);
        }


        var lastPlayed = context.LastPlayedCards ?? State.LastPlayedCards;

        if (lastPlayed is null or { Length: 0 })

        {
            return DecideLandlordLead(context);
        }


        return DecideLandlordFollow(context, lastPlayed);
    }


    private BotDecision DecideLandlordLead(PlayContext context)

    {
        var attempt = State.LandlordLeadAttempt % 4;

        State.LandlordLeadAttempt++;


        var cards = attempt switch

        {
            0 => EncodeCards(FindSmallestSingle()),

            1 => EncodeCards(FindSmallestPair()),

            2 => EncodeCards(FindSmallestTriple()),

            3 => EncodeCards(FindSmallestBomb()),

            _ => null,
        };


        cards ??= EncodeCards(FindSmallestSingle())
                  ?? throw new InvalidOperationException("Landlord lead requested with an empty hand.");


        return BotDecision.Play(context.NextPlayer, cards);
    }


    private BotDecision DecideLandlordFollow(PlayContext context, byte[] lastPlayed)

    {
        byte[]? cards = null;


        if (lastPlayed.Length == 1)

        {
            cards = EncodeCards(FindSmallestBeatingSingle(new GameCard(lastPlayed[0])));
        }

        else if (lastPlayed.Length == 2 && IsRocket(lastPlayed))

        {
            cards = EncodeCards(FindRocket());
        }

        else if (lastPlayed.Length == 2 && lastPlayed[0] == lastPlayed[1])

        {
            cards = EncodeCards(FindSmallestBeatingPair(new GameCard(lastPlayed[0])));
        }

        else if (lastPlayed.Length == 3 && lastPlayed[0] == lastPlayed[1] && lastPlayed[1] == lastPlayed[2])

        {
            cards = EncodeCards(FindSmallestBeatingTriple(new GameCard(lastPlayed[0])));
        }

        else if (lastPlayed.Length == 4 && lastPlayed.All(c => c == lastPlayed[0]))

        {
            cards = EncodeCards(FindSmallestBeatingBomb(new GameCard(lastPlayed[0])));
        }


        if (cards is null)

        {
            return BotDecision.Pass(context.NextPlayer, context.PassPlayer);
        }


        return BotDecision.Play(context.NextPlayer, cards);
    }


    private GameCard? FindSmallestSingle()

    {
        return State.Hand.Count > 0 ? State.Hand[0] : null;
    }


    private IReadOnlyList<GameCard>? FindSmallestPair()

    {
        return FindSmallestGroup(minCount: 2, take: 2);
    }


    private IReadOnlyList<GameCard>? FindSmallestTriple()

    {
        return FindSmallestGroup(minCount: 3, take: 3);
    }


    private IReadOnlyList<GameCard>? FindSmallestBomb()

    {
        var quad = FindSmallestGroup(minCount: 4, take: 4);

        if (quad is not null)

        {
            return quad;
        }


        return FindRocket();
    }


    private IReadOnlyList<GameCard>? FindRocket()

    {
        var small = State.Hand.FirstOrDefault(c => c.Byte == SmallJoker);

        var big = State.Hand.FirstOrDefault(c => c.Byte == BigJoker);

        if (small.Byte != SmallJoker || big.Byte != BigJoker)

        {
            return null;
        }


        return new[] { small, big };
    }


    private IReadOnlyList<GameCard>? FindSmallestGroup(int minCount, int take)

    {
        foreach (var group in State.Hand.GroupBy(c => c.Value).OrderBy(g => g.Key))

        {
            if (group.Count() < minCount)

            {
                continue;
            }


            return group.OrderBy(c => c, Comparer<GameCard>.Create(CardCodec.Compare)).Take(take).ToList();
        }


        return null;
    }


    private GameCard? FindSmallestBeatingSingle(GameCard target)

    {
        GameCard? best = null;

        foreach (var card in State.Hand)

        {
            if (!CardCodec.Beats(card, target))

            {
                continue;
            }


            if (best is null || CardCodec.Compare(card, best.Value) < 0)

            {
                best = card;
            }
        }


        return best;
    }


    private IReadOnlyList<GameCard>? FindSmallestBeatingPair(GameCard target)

    {
        return FindSmallestBeatingGroup(target, minCount: 2, take: 2);
    }


    private IReadOnlyList<GameCard>? FindSmallestBeatingTriple(GameCard target)

    {
        return FindSmallestBeatingGroup(target, minCount: 3, take: 3);
    }


    private IReadOnlyList<GameCard>? FindSmallestBeatingBomb(GameCard target)

    {
        var quad = FindSmallestBeatingGroup(target, minCount: 4, take: 4);

        if (quad is not null)

        {
            return quad;
        }


        return FindRocket();
    }


    private IReadOnlyList<GameCard>? FindSmallestBeatingGroup(GameCard target, int minCount, int take)

    {
        foreach (var group in State.Hand.GroupBy(c => c.Value).OrderBy(g => g.Key))

        {
            if (group.Count() < minCount)

            {
                continue;
            }


            var cards = group.OrderBy(c => c, Comparer<GameCard>.Create(CardCodec.Compare)).Take(take).ToList();

            if (CardCodec.Beats(cards[0], target))

            {
                return cards;
            }
        }


        return null;
    }


    private static bool IsRocket(byte[] cards) =>
        cards.Length == 2 && cards.Contains(SmallJoker) && cards.Contains(BigJoker);


    private static byte[]? EncodeCards(GameCard? card) =>
        card is null ? null : new[] { card.Value.Byte };


    private static byte[]? EncodeCards(IReadOnlyList<GameCard>? cards) =>
        cards is null || cards.Count == 0 ? null : CardCodec.Encode(cards);


    private void RemoveFromHand(byte[] played)

    {
        foreach (var b in played)

        {
            for (var i = 0; i < State.Hand.Count; i++)

            {
                if (State.Hand[i].Byte != b)

                {
                    continue;
                }


                State.Hand.RemoveAt(i);

                break;
            }
        }


        State.Hand.Sort(CardCodec.Compare);
    }
}