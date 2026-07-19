namespace Orient.Rpc.Transport;

public sealed class TcpChannelHostOptions
{
    public const int DefaultIoThreadCount = 1;
    public const int DefaultConnectTimeoutSeconds = 10;

    public int IoThreadCount { get; init; } = DefaultIoThreadCount;

    public int ConnectTimeoutSeconds { get; init; } = DefaultConnectTimeoutSeconds;

    public bool TcpNoDelay { get; init; } = true;

    public bool ChannelLoggingEnabled { get; init; } = false;

    public string LoggingName { get; init; } = "tcp-channel";

    public void Validate()
    {
        if (IoThreadCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(IoThreadCount),
                IoThreadCount,
                "TcpChannelHostOptions.IoThreadCount must be positive.");
        }

        if (ConnectTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ConnectTimeoutSeconds),
                ConnectTimeoutSeconds,
                "TcpChannelHostOptions.ConnectTimeoutSeconds must be positive.");
        }

        if (string.IsNullOrWhiteSpace(LoggingName))
        {
            throw new ArgumentException(
                "TcpChannelHostOptions.LoggingName must not be empty.",
                nameof(LoggingName));
        }
    }
}
