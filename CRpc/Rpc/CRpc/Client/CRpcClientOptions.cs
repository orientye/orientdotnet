namespace CRpc.Rpc.CRpc.Client;

public sealed class CRpcClientOptions
{
    public const int DefaultMaxFrameLength = 65536;

    public const int DefaultHeartbeatIntervalSeconds = 15;

    public const int DefaultConnectTimeoutSeconds = 10;

    public const int DefaultIoThreadCount = 1;

    public const int DefaultCallTimeoutMilliseconds = 5000;

    public int IoThreadCount { get; init; } = DefaultIoThreadCount;

    public int ConnectTimeoutSeconds { get; init; } = DefaultConnectTimeoutSeconds;

    public bool HeartbeatEnabled { get; init; } = true;

    public int HeartbeatIntervalSeconds { get; init; } = DefaultHeartbeatIntervalSeconds;

    public int MaxFrameLength { get; init; } = DefaultMaxFrameLength;

    public int CallTimeoutMilliseconds { get; init; } = DefaultCallTimeoutMilliseconds;

    public void Validate()
    {
        if (HeartbeatIntervalSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(HeartbeatIntervalSeconds),
                HeartbeatIntervalSeconds,
                "Heartbeat interval must be positive.");
        }
    }
}
