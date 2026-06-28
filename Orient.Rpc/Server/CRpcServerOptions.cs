using System.Net;
using Orient.Rpc.Client;
using Orient.Rpc.Codec;
using DotNetty.Transport.Channels;

namespace Orient.Rpc.Server;

public sealed class CRpcServerOptions
{
    public const int DefaultPort = 7999;

    public const int DefaultMaxFrameLength = 65536;

    public const int DefaultBossThreadCount = 1;

    public const int DefaultWorkerThreadCount = 1;

    public const int DefaultSoBacklog = 8192;

    public const int DefaultReadIdleSeconds = 45;

    public const int MinPort = 0;

    public const int MaxPort = 65535;

    public const int MaxMaxFrameLength = 16 * 1024 * 1024;

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
        if (Port != MinPort && (Port < 1 || Port > MaxPort))
        {
            throw new ArgumentOutOfRangeException(
                nameof(Port),
                Port,
                "CRpcServerOptions.Port must be 0 (ephemeral) or between 1 and 65535.");
        }

        if (MaxFrameLength < CRpcMessage.MinFrameLength || MaxFrameLength > MaxMaxFrameLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxFrameLength),
                MaxFrameLength,
                $"CRpcServerOptions.MaxFrameLength must be between {CRpcMessage.MinFrameLength} and {MaxMaxFrameLength}.");
        }

        if (BossThreadCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BossThreadCount),
                BossThreadCount,
                "CRpcServerOptions.BossThreadCount must be positive.");
        }

        if (WorkerThreadCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(WorkerThreadCount),
                WorkerThreadCount,
                "CRpcServerOptions.WorkerThreadCount must be positive.");
        }

        if (SoBacklog <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SoBacklog),
                SoBacklog,
                "CRpcServerOptions.SoBacklog must be positive.");
        }

        if (!HeartbeatEnabled)
        {
            return;
        }

        if (ReadIdleSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ReadIdleSeconds),
                ReadIdleSeconds,
                "CRpcServerOptions.ReadIdleSeconds must be positive when heartbeat is enabled.");
        }

        if (ReadIdleSeconds < clientHeartbeatIntervalSeconds * 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ReadIdleSeconds),
                ReadIdleSeconds,
                "CRpcServerOptions.ReadIdleSeconds must be at least twice the client heartbeat interval.");
        }
    }
}
