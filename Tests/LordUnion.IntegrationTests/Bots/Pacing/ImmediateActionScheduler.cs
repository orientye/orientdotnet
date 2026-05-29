using CRpc.Async;

namespace LordUnion.IntegrationTests.Bots.Pacing;

public sealed class ImmediateActionScheduler : IActionScheduler
{
    public static ImmediateActionScheduler Instance { get; } = new();

    public CRpcTask WaitBeforeSendAsync(BotSendContext context, CRpcLoop loop)
    {
        _ = context;
        return CRpcTask.CompletedTask(loop);
    }
}