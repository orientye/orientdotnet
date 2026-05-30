using CRpc.Async;
using CRpc.Transport;
using DotNetty.Transport.Channels;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Protocol;

namespace LordUnion.IntegrationTests.Sessions;

public sealed class GameServerDotNettyTransport : IGameServerTransport, IAsyncDisposable
{
    private readonly ServerProtocolCodec codec;
    private readonly IEventLoopGroup? sharedEventLoopGroup;
    private TcpChannelHost? host;
    private AccountSession? session;

    public GameServerDotNettyTransport(ServerProtocolCodec? codec = null)
        : this(codec, sharedEventLoopGroup: null)
    {
    }

    public GameServerDotNettyTransport(ServerProtocolCodec? codec, IEventLoopGroup? sharedEventLoopGroup)
    {
        this.codec = codec ?? new ServerProtocolCodec();
        this.sharedEventLoopGroup = sharedEventLoopGroup;
    }

    public void BindIncomingHandler(AccountSession session, ServerProtocolCodec codec)
    {
        _ = codec;
        this.session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public CRpcTask ConnectAsync(
        ServerConfig server,
        TimeSpan timeout,
        CRpcLoop loop,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(loop);
        _ = cancellationToken;

        var options = new TcpChannelHostOptions
        {
            ConnectTimeoutSeconds = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds)),
            LoggingName = session is null
                ? "game-server"
                : $"game-server-{session.Alias}",
        };

        return ConnectCoreAsync(server, loop, options);
    }

    private async CRpcTask ConnectCoreAsync(ServerConfig server, CRpcLoop ownerLoop, TcpChannelHostOptions options)
    {
        host = new TcpChannelHost(ownerLoop, new GameServerPipelineFactory(), options, sharedEventLoopGroup)
        {
            InboundMessageReceived = HandleInboundMessage,
            ChannelBecameInactive = HandleChannelInactive,
            ChannelExceptionCaught = HandleChannelException
        };

        await host.ConnectAsync(server.Host, server.Port);
    }

    public CRpcTask SendAsync(byte[] packet, CRpcLoop ownerLoop)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(ownerLoop);

        if (host is null)
        {
            throw new InvalidOperationException("Transport is not connected.");
        }

        var frame = BuildOutboundFrame(packet);
        return host.WriteAndFlushAsync(frame);
    }

    public CRpcTask DisconnectAsync(CRpcLoop ownerLoop)
    {
        ArgumentNullException.ThrowIfNull(ownerLoop);

        if (host is null)
        {
            return CRpcTask.CompletedTask(ownerLoop);
        }

        return host.CloseAsync();
    }

    public ValueTask DisposeAsync()
    {
        if (host is null)
        {
            return ValueTask.CompletedTask;
        }

        var activeHost = host;
        host = null;
        return activeHost.DisposeAsync();
    }

    private GameServerFrame BuildOutboundFrame(byte[] packet)
    {
        if (packet.Length < ServerPacketFrame.HeaderLength)
        {
            throw new ArgumentException(
                $"Packet must include the {ServerPacketFrame.HeaderLength}-byte game-server header.",
                nameof(packet));
        }

        var frame = ServerPacketFrame.DecodeHeader(packet.AsSpan(0, ServerPacketFrame.HeaderLength));
        var expectedLength = ServerPacketFrame.HeaderLength + frame.BodyLength;
        if (packet.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Packet length {packet.Length} does not match header length {expectedLength}.",
                nameof(packet));
        }

        var body = packet.AsSpan(ServerPacketFrame.HeaderLength, frame.BodyLength).ToArray();
        return new GameServerFrame(frame.Header0, body);
    }

    private void HandleInboundMessage(object message)
    {
        if (message is not GameServerFrame frame)
        {
            HandleChannelException(new InvalidOperationException(
                $"Unexpected game-server inbound message type '{message.GetType().FullName}'."));
            return;
        }

        var activeSession = session
                            ?? throw new InvalidOperationException("Incoming handler is not bound.");

        var packet = ServerPacketFrame.EncodeFrame(frame.Header0, frame.Body);
        var protocolMessage = codec.DecodePacket(
            packet,
            new ProtocolDecodeContext
            {
                AccountAlias = activeSession.Alias,
                Phase = activeSession.CurrentPhase,
            });

        activeSession.DeliverIncomingMessage(protocolMessage);
    }

    private void HandleChannelInactive()
    {
        session?.SetState(AccountSessionState.Failed);
    }

    private void HandleChannelException(Exception exception)
    {
        Console.Error.WriteLine(
            $"GameServerDotNettyTransport: channel failed for account '{session?.Alias ?? "<unbound>"}': {exception.Message}");
        session?.SetState(AccountSessionState.Failed);
    }

    internal GameServerFrame BuildOutboundFrameForTesting(byte[] packet)
    {
        return BuildOutboundFrame(packet);
    }

    internal void DeliverFrameForTesting(GameServerFrame frame)
    {
        HandleInboundMessage(frame);
    }
}