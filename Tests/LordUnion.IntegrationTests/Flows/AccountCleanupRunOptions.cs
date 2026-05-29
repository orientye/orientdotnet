namespace LordUnion.IntegrationTests.Flows;

public sealed class AccountCleanupRunOptions
{
    public const int DefaultDrainWindowMs = 2000;

    public IReadOnlyList<uint> KnownMatchIds { get; init; } = Array.Empty<uint>();

    public int DrainWindowMs { get; init; } = DefaultDrainWindowMs;

    /// <summary>Allows cleanup after a finished game (session may be <see cref="Sessions.AccountSessionState.Finished"/>).</summary>
    public bool PostGame { get; init; }

    public static AccountCleanupRunOptions PreSignup(int drainWindowMs = DefaultDrainWindowMs) =>
        new() { DrainWindowMs = drainWindowMs };

    public static AccountCleanupRunOptions PostGameCleanup(params uint[] knownMatchIds) =>
        new()
        {
            PostGame = true,
            KnownMatchIds = knownMatchIds.Where(id => id > 0).ToArray(),
            DrainWindowMs = 0,
        };
}
