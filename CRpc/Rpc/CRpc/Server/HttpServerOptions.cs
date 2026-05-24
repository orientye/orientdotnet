using System.Net;

namespace CRpc.Rpc.CRpc.Server;

public sealed class HttpServerOptions
{
    public const int DefaultPort = 8080;

    public const int DefaultBossThreadCount = 1;

    public const int DefaultWorkerThreadCount = 1;

    public const int DefaultSoBacklog = 8192;

    public IPAddress Address { get; init; } = IPAddress.Any;

    public int Port { get; init; } = DefaultPort;

    public int MaxContentLength { get; init; } = 1024 * 1024;

    public int BossThreadCount { get; init; } = DefaultBossThreadCount;

    public int WorkerThreadCount { get; init; } = DefaultWorkerThreadCount;

    public int SoBacklog { get; init; } = DefaultSoBacklog;
}
