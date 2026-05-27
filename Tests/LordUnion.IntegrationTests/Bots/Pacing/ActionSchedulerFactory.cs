using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Scenarios;

namespace LordUnion.IntegrationTests.Bots.Pacing;

public static class ActionSchedulerFactory
{
    public static IActionScheduler Create(
        BotConfig? botConfig,
        TimeoutConfig timeouts,
        ScenarioRunOptions? options = null)
    {
        if (options?.SchedulerOverride is { } schedulerOverride)
        {
            return schedulerOverride;
        }

        if (options?.SkipBotPacing == true)
        {
            return ImmediateActionScheduler.Instance;
        }

        var mode = ParseMode(botConfig?.Pacing);
        var pacingOptions = botConfig?.PacingOptions ?? new BotPacingOptions();

        return mode switch
        {
            BotPacingMode.Immediate => ImmediateActionScheduler.Instance,
            BotPacingMode.HumanRandom => new HumanRandomActionScheduler(
                pacingOptions,
                timeouts.GameActionTimeout),
            BotPacingMode.ServerCountdown => new HumanRandomActionScheduler(
                pacingOptions,
                timeouts.GameActionTimeout),
            BotPacingMode.ReplayTimeline => new HumanRandomActionScheduler(
                pacingOptions,
                timeouts.GameActionTimeout),
            _ => new HumanRandomActionScheduler(pacingOptions, timeouts.GameActionTimeout),
        };
    }

    public static BotPacingMode ParseMode(string? pacing)
    {
        if (string.IsNullOrWhiteSpace(pacing))
        {
            return BotPacingMode.HumanRandom;
        }

        return pacing.Trim().ToLowerInvariant() switch
        {
            "immediate" or "none" or "0" => BotPacingMode.Immediate,
            "human-random" or "humanrandom" or "random" => BotPacingMode.HumanRandom,
            "server-countdown" or "servercountdown" => BotPacingMode.ServerCountdown,
            "replay" or "replay-timeline" => BotPacingMode.ReplayTimeline,
            _ => BotPacingMode.HumanRandom,
        };
    }
}
