namespace LordUnion.IntegrationTests.Flows;

/// <summary>
/// Table-level game phase: after any seat receives a game-end signal, remaining seats
/// may be completed when <see cref="GracePeriod"/> elapses (without waiting for each seat's
/// full <c>GameOverTimeout</c>).
/// </summary>
internal sealed class TableGamePhaseCoordinator
{
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(10);

    private readonly TimeSpan gracePeriod;
    private DateTimeOffset? firstEndUtc;

    public TableGamePhaseCoordinator(TimeSpan? gracePeriod = null)
    {
        this.gracePeriod = gracePeriod ?? DefaultGracePeriod;
        if (this.gracePeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(gracePeriod), "Grace period must be positive.");
        }
    }

    public bool HasFirstEnd => firstEndUtc is not null;

    public void NotifyFirstGameEnded()
    {
        firstEndUtc ??= DateTimeOffset.UtcNow;
    }

    public bool IsGraceExpired =>
        firstEndUtc is { } started && DateTimeOffset.UtcNow >= started + gracePeriod;

}
