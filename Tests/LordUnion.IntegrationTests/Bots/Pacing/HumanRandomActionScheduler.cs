using CRpc.Async;
using LordUnion.IntegrationTests.Config;

namespace LordUnion.IntegrationTests.Bots.Pacing;

/// <summary>
/// Simulates human think time with configurable random ranges (Unity-like 1–3s bid, 2–6s play).
/// </summary>
public sealed class HumanRandomActionScheduler : IActionScheduler
{
    private readonly BotPacingOptions options;
    private readonly int maxDelayMs;
    private readonly Random random;

    public HumanRandomActionScheduler(
        BotPacingOptions options,
        TimeSpan maxActionTimeout,
        Random? random = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        maxDelayMs = ToPositiveMilliseconds(maxActionTimeout);
        this.random = random ?? new Random();
    }

    public CRpcTask WaitBeforeSendAsync(BotSendContext context, CRpcLoop loop)
    {
        var delayMs = GetDelayMilliseconds(context);
        if (delayMs <= 0)
        {
            return CRpcTask.CompletedTask(loop);
        }

        return CRpcTask.Delay(delayMs, loop);
    }

    public int GetDelayMilliseconds(BotSendContext context)
    {
        if (context.Decision.Kind == BotDecisionKind.Ready)
        {
            return 0;
        }

        var (minMs, maxMs) = context.Decision.Kind == BotDecisionKind.Bid
            ? (options.BidMinMs, options.BidMaxMs)
            : (options.PlayMinMs, options.PlayMaxMs);

        if (maxMs < minMs)
        {
            maxMs = minMs;
        }

        var baseDelay = minMs >= maxMs
            ? minMs
            : random.Next(minMs, maxMs + 1);

        var jitter = GetAliasJitterMs(context.Session.Alias, options.AliasJitterMaxMs);
        var total = baseDelay + jitter;

        if (maxDelayMs > 0)
        {
            total = Math.Min(total, maxDelayMs);
        }

        return total;
    }

    internal static int GetAliasJitterMs(string alias, int maxJitterMs)
    {
        if (maxJitterMs <= 0 || string.IsNullOrEmpty(alias))
        {
            return 0;
        }

        var hash = 0;
        foreach (var ch in alias)
        {
            hash = (hash * 31) + ch;
        }

        return Math.Abs(hash) % (maxJitterMs + 1);
    }

    private static int ToPositiveMilliseconds(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return 0;
        }

        return (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue);
    }
}
