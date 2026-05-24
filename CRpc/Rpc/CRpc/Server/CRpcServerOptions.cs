using System.Net;

namespace CRpc.Rpc.CRpc.Server;

public sealed class CRpcServerOptions
{
    public const int DefaultPort = 7999;

    public const int DefaultMaxFrameLength = 32768;

    public const int DefaultHashLength = 16;

    public const int DefaultCompressThreshold = 512;

    public const int DefaultBossThreadCount = 1;

    public const int DefaultWorkerThreadCount = 1;

    public const int DefaultSoBacklog = 8192;

    public IPAddress Address { get; init; } = IPAddress.Any;

    public int Port { get; init; } = DefaultPort;

    public int MaxFrameLength { get; init; } = DefaultMaxFrameLength;

    public int HashLength { get; init; } = DefaultHashLength;

    public int CompressThreshold { get; init; } = DefaultCompressThreshold;

    public int BossThreadCount { get; init; } = DefaultBossThreadCount;

    public int WorkerThreadCount { get; init; } = DefaultWorkerThreadCount;

    public int SoBacklog { get; init; } = DefaultSoBacklog;
}
