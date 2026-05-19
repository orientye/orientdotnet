using System.Net;

namespace CRpc.Rpc.CRpc.Server;

public sealed class HttpServerOptions
{
    public IPAddress Address { get; init; } = IPAddress.Any;

    public int Port { get; init; } = 8080;

    public int MaxContentLength { get; init; } = 1024 * 1024;
}
