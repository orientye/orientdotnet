using CRpc.Async;
using LordUnion.IntegrationTests.Bots;
using LordUnion.IntegrationTests.Bots.Pacing;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Flows;
using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Sessions;

namespace LordUnion.IntegrationTests.Scenarios;

public sealed class ScenarioRunOptions
{
    public bool UseLiveTransport { get; init; }

    public bool SkipAccountCleanup { get; init; }

    public bool SkipBotPacing { get; init; }

    public IBotPolicy? PolicyOverride { get; init; }

    public IActionScheduler? SchedulerOverride { get; init; }

    public IScenarioTransportFactory? TransportFactory { get; init; }

    /// <summary>
    /// Optional override for tests that stub or abbreviate in-game flow.
    /// </summary>
    public Func<
        AccountSession,
        MinimalLandlordBot,
        ILordGameVariant,
        IGameServerTransport?,
        TimeSpan,
        CRpcTask<GameFlowResult>>? GameFlowOverride { get; init; }

    /// <summary>
    /// Optional factory used by fake tests to push StartGameClientAck after enter-match begins waiting.
    /// </summary>
    public Func<AccountSession, ProtocolMessage>? MatchStartAckFactory { get; init; }
}

public interface IScenarioTransportFactory
{
    IGameServerTransport CreateTransport(AccountSession session, AccountConfig account);
}

public sealed class FakeScenarioTransportFactory : IScenarioTransportFactory
{
    private readonly ServerProtocolCodec codec;
    private readonly IReadOnlyDictionary<string, FakeTransportScript> scriptsByAlias;

    public FakeScenarioTransportFactory(
        IReadOnlyDictionary<string, FakeTransportScript> scriptsByAlias,
        ServerProtocolCodec? codec = null)
    {
        this.scriptsByAlias = scriptsByAlias ?? throw new ArgumentNullException(nameof(scriptsByAlias));
        this.codec = codec ?? new ServerProtocolCodec();
    }

    public IGameServerTransport CreateTransport(AccountSession session, AccountConfig account)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(account);

        var transport = new FakeGameServerTransport();
        transport.BindIncomingHandler(session, codec);

        if (scriptsByAlias.TryGetValue(account.Alias, out var script))
        {
            transport.OnPacketSentAsync = (packet, loop) =>
                script.HandlePacketAsync(transport, session, packet, loop);
        }

        return transport;
    }
}

/// <summary>
/// Per-account fake-server script invoked when the transport sends a packet.
/// </summary>
public sealed class FakeTransportScript
{
    public Func<FakeGameServerTransport, AccountSession, byte[], CRpcLoop, CRpcTask>? OnPacketSentAsync { get; init; }

    public CRpcTask HandlePacketAsync(
        FakeGameServerTransport transport,
        AccountSession session,
        byte[] packet,
        CRpcLoop loop)
    {
        if (OnPacketSentAsync is null)
        {
            return CRpcTask.CompletedTask(loop);
        }

        return OnPacketSentAsync(transport, session, packet, loop);
    }
}