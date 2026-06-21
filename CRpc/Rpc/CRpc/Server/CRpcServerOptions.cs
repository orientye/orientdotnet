using System.Net;
using CRpc.Rpc.CRpc.Client;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Server;

public sealed class CRpcServerOptions
{
    public const int DefaultPort = 7999;

    public const int DefaultMaxFrameLength = 65536;

    public const int DefaultBossThreadCount = 1;

    public const int DefaultWorkerThreadCount = 1;

    public const int DefaultSoBacklog = 8192;

    public const int DefaultReadIdleSeconds = 45;

    public IPAddress Address { get; init; } = IPAddress.Any;

    public int Port { get; init; } = DefaultPort;

    public int MaxFrameLength { get; init; } = DefaultMaxFrameLength;

    public int BossThreadCount { get; init; } = DefaultBossThreadCount;

    public int WorkerThreadCount { get; init; } = DefaultWorkerThreadCount;

    public int SoBacklog { get; init; } = DefaultSoBacklog;

    public bool HeartbeatEnabled { get; init; } = true;

    public int ReadIdleSeconds { get; init; } = DefaultReadIdleSeconds;

    /// <summary>
    /// Optional factory for creating the channel handler added to each child channel's pipeline.
    /// Defaults to <see cref="CRpcServerHandler"/> when null.
    /// </summary>
    public Func<CRpcServer, IChannelHandler>? HandlerFactory { get; init; }

    public void Validate(int clientHeartbeatIntervalSeconds = CRpcClientOptions.DefaultHeartbeatIntervalSeconds)
    {
        if (ReadIdleSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ReadIdleSeconds),
                ReadIdleSeconds,
                "Read idle must be positive.");
        }

        if (ReadIdleSeconds < clientHeartbeatIntervalSeconds * 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ReadIdleSeconds),
                ReadIdleSeconds,
                "Read idle must be at least twice the client heartbeat interval.");
        }
    }
}
