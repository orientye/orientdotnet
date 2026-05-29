using CRpc.Async;

namespace LordUnion.IntegrationTests.Bots.Pacing;

public interface IActionScheduler
{
    CRpcTask WaitBeforeSendAsync(BotSendContext context, CRpcLoop loop);
}