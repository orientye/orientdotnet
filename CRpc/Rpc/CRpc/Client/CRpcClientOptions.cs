namespace CRpc.Rpc.CRpc.Client;

public sealed class CRpcClientOptions
{
    public const int DefaultMaxFrameLength = 32768;

    public const int DefaultHashLength = 16;

    public const int DefaultCompressThreshold = 512;

    public const int DefaultHeartbeatIdleSeconds = 60;

    public const int DefaultConnectTimeoutSeconds = 10;

    public const int DefaultIoThreadCount = 1;

    public const int DefaultCallTimeoutMilliseconds = 5000;

    public int IoThreadCount { get; init; } = DefaultIoThreadCount;

    public int ConnectTimeoutSeconds { get; init; } = DefaultConnectTimeoutSeconds;

    public int HeartbeatIdleSeconds { get; init; } = DefaultHeartbeatIdleSeconds;

    public int MaxFrameLength { get; init; } = DefaultMaxFrameLength;

    public int HashLength { get; init; } = DefaultHashLength;

    public int CompressThreshold { get; init; } = DefaultCompressThreshold;

    public int CallTimeoutMilliseconds { get; init; } = DefaultCallTimeoutMilliseconds;
}
