using Orient.Logging;
using Orient.Rpc.Codec;

namespace Orient.Rpc.Client;

public sealed class CRpcClientOptions
{
    public const int DefaultMaxFrameLength = 65536;

    public const int DefaultHeartbeatIntervalSeconds = 15;

    public const int DefaultConnectTimeoutSeconds = 10;

    public const int DefaultIoThreadCount = 1;

    public const int DefaultCallTimeoutMilliseconds = 5000;

    public const int MaxMaxFrameLength = 16 * 1024 * 1024;

    public int IoThreadCount { get; init; } = DefaultIoThreadCount;

    public int ConnectTimeoutSeconds { get; init; } = DefaultConnectTimeoutSeconds;

    public bool HeartbeatEnabled { get; init; } = true;

    public int HeartbeatIntervalSeconds { get; init; } = DefaultHeartbeatIntervalSeconds;

    public int MaxFrameLength { get; init; } = DefaultMaxFrameLength;

    public int CallTimeoutMilliseconds { get; init; } = DefaultCallTimeoutMilliseconds;

    public IOrientLoggerFactory? LoggerFactory { get; init; }

    public void Validate()
    {
        if (IoThreadCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(IoThreadCount),
                IoThreadCount,
                "CRpcClientOptions.IoThreadCount must be positive.");
        }

        if (ConnectTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ConnectTimeoutSeconds),
                ConnectTimeoutSeconds,
                "CRpcClientOptions.ConnectTimeoutSeconds must be positive.");
        }

        if (MaxFrameLength < CRpcMessage.MinFrameLength || MaxFrameLength > MaxMaxFrameLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxFrameLength),
                MaxFrameLength,
                $"CRpcClientOptions.MaxFrameLength must be between {CRpcMessage.MinFrameLength} and {MaxMaxFrameLength}.");
        }

        if (CallTimeoutMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CallTimeoutMilliseconds),
                CallTimeoutMilliseconds,
                "CRpcClientOptions.CallTimeoutMilliseconds must be positive.");
        }

        if (!HeartbeatEnabled)
        {
            return;
        }

        if (HeartbeatIntervalSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(HeartbeatIntervalSeconds),
                HeartbeatIntervalSeconds,
                "Heartbeat interval must be positive.");
        }
    }
}
