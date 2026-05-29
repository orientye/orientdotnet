namespace LordUnion.IntegrationTests.Bots;

/// <summary>
/// Minimal bot-visible game state for classic Dou Dizhu V1.
/// </summary>
public sealed class BotGameState
{
    public List<GameCard> Hand { get; } = new();

    public uint MySeat { get; set; }

    /// <summary>
    /// Declared landlord seat for the current hand, or <see langword="null"/> before bottom cards.
    /// </summary>
    public uint? LordSeat { get; set; }

    /// <summary>
    /// Cards in the current trick to follow, or <see langword="null"/> when leading.
    /// </summary>
    public byte[]? LastPlayedCards { get; set; }

    /// <summary>
    /// Set after the landlord's first takeout decision so a later kick <c>TurnStarted</c> does not double-lead.
    /// </summary>
    public bool LandlordFirstLeadDone { get; set; }

    /// <summary>
    /// Rotates landlord lead shape: single, pair, triple, bomb.
    /// </summary>
    public int LandlordLeadAttempt { get; set; }
}