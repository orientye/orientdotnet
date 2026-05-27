using System.Net.Sockets;
using CRpc.Async;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Protocol;

namespace LordUnion.IntegrationTests.Sessions;

public sealed class GameServerTcpTransport : IGameServerTransport, IAsyncDisposable
{
    private readonly ServerProtocolCodec codec;
    private TcpClient? client;
    private NetworkStream? stream;
    private CancellationTokenSource? readCts;
    private AccountSession? session;
    private CRpcLoop? loop;
    private bool receiveLoopStarted;

    public GameServerTcpTransport(ServerProtocolCodec? codec = null)
    {
        this.codec = codec ?? new ServerProtocolCodec();
    }

    public CRpcTask ConnectAsync(
        ServerConfig server,
        TimeSpan timeout,
        CRpcLoop loop,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(loop);
        this.loop = loop;

        return CRpcTask.FromTask(ConnectTcpAsync(server, timeout, cancellationToken), loop);
    }

    public CRpcTask SendAsync(byte[] packet, CRpcLoop loop)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(loop);

        if (stream is null)
        {
            throw new InvalidOperationException("Transport is not connected.");
        }

        return CRpcTask.FromTask(stream.WriteAsync(packet, CancellationToken.None).AsTask(), loop);
    }

    public CRpcTask DisconnectAsync(CRpcLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        return CRpcTask.FromTask(DisconnectCoreAsync(), loop);
    }

    public void BindIncomingHandler(AccountSession session, ServerProtocolCodec codec)
    {
        _ = codec;
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        TryStartReceiveLoop();
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectCoreAsync();
    }

    private async Task ConnectTcpAsync(ServerConfig server, TimeSpan timeout, CancellationToken cancellationToken)
    {
        client?.Dispose();
        client = new TcpClient();

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(timeout);
        await client.ConnectAsync(server.Host, server.Port, connectCts.Token);

        stream = client.GetStream();
        var timeoutMs = (int)Math.Clamp(timeout.TotalMilliseconds, 1, int.MaxValue);
        stream.ReadTimeout = timeoutMs;
        stream.WriteTimeout = timeoutMs;
        TryStartReceiveLoop();
    }

    private void TryStartReceiveLoop()
    {
        if (receiveLoopStarted || session is null || loop is null || stream is null)
        {
            return;
        }

        receiveLoopStarted = true;
        readCts?.Cancel();
        readCts = new CancellationTokenSource();
        _ = ReadLoopAsync(readCts.Token);
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var activeSession = session;
        var activeLoop = loop;
        var activeStream = stream;
        if (activeSession is null || activeLoop is null || activeStream is null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await GameServerFrameReader.ReadFrameAsync(activeStream, cancellationToken);
                var packet = ServerPacketFrame.EncodeFrame(frame.Header0, frame.Body);
                var message = codec.DecodePacket(
                    packet,
                    new ProtocolDecodeContext
                    {
                        AccountAlias = activeSession.Alias,
                        Phase = activeSession.CurrentPhase,
                    });

                activeSession.DeliverIncomingMessage(message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            activeLoop.Post(() =>
            {
                Console.Error.WriteLine(
                    $"GameServerTcpTransport: receive loop failed for account '{activeSession.Alias}': {ex.Message}");
                activeSession.SetState(AccountSessionState.Failed);
            });
        }
    }

    private async Task DisconnectCoreAsync()
    {
        receiveLoopStarted = false;
        readCts?.Cancel();
        readCts?.Dispose();
        readCts = null;

        if (stream is not null)
        {
            await stream.DisposeAsync();
            stream = null;
        }

        client?.Dispose();
        client = null;
    }
}
