namespace CRpc.Rpc.CRpc.Client;

public sealed class CRpcClientOptions
{
    public const int DefaultMaxFrameLength = 65536;

    public const int DefaultHeartbeatIdleSeconds = 60;

    public const int DefaultConnectTimeoutSeconds = 10;

    public const int DefaultIoThreadCount = 1;

    public const int DefaultCallTimeoutMilliseconds = 5000;

    public int IoThreadCount { get; init; } = DefaultIoThreadCount;

    public int ConnectTimeoutSeconds { get; init; } = DefaultConnectTimeoutSeconds;

    public int HeartbeatIdleSeconds { get; init; } = DefaultHeartbeatIdleSeconds;

    public int MaxFrameLength { get; init; } = DefaultMaxFrameLength;

    public int CallTimeoutMilliseconds { get; init; } = DefaultCallTimeoutMilliseconds;
}
