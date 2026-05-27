using CRpc.Async;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Protocol;

namespace LordUnion.IntegrationTests.Sessions;

public sealed class FakeGameServerTransport : IGameServerTransport
{
    private readonly List<byte[]> sentPackets = new();
    private AccountSession? session;
    private ServerProtocolCodec? codec;

    public IReadOnlyList<byte[]> SentPackets => sentPackets;

    public Func<byte[], CRpcLoop, CRpcTask>? OnPacketSentAsync { get; set; }

    public CRpcTask ConnectAsync(
        ServerConfig server,
        TimeSpan timeout,
        CRpcLoop loop,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(loop);
        return CRpcTask.CompletedTask(loop);
    }

    public async CRpcTask SendAsync(byte[] packet, CRpcLoop loop)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(loop);
        sentPackets.Add(packet);

        if (OnPacketSentAsync is not null)
        {
            await OnPacketSentAsync(packet, loop);
        }
    }

    public CRpcTask DisconnectAsync(CRpcLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        return CRpcTask.CompletedTask(loop);
    }

    public void BindIncomingHandler(AccountSession session, ServerProtocolCodec codec)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
    }

    public void DeliverIncomingMessage(ProtocolMessage message)
    {
        if (session is null)
        {
            throw new InvalidOperationException("BindIncomingHandler must be called before delivering messages.");
        }

        session.DeliverIncomingMessage(message);
    }

    public ProtocolMessage DecodeSentPacket(byte[] packet, ProtocolDecodeContext context)
    {
        if (codec is null)
        {
            throw new InvalidOperationException("BindIncomingHandler must be called before decoding packets.");
        }

        return codec.DecodePacket(packet, context);
    }
}
