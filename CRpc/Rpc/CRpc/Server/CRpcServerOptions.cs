using System.Net;

namespace CRpc.Rpc.CRpc.Server;

public sealed class CRpcServerOptions
{
    public IPAddress Address { get; init; } = IPAddress.Any;

    public int Port { get; init; } = 7999;

    public int MaxFrameLength { get; init; } = 32768;

    public int HashLength { get; init; } = 16;
}
