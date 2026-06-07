using CRpc.Async;
using CRpc.Rpc.CRpc.Client;
using CRpc.Rpc.CRpc.Server;

namespace GateWay;

public sealed class GateWayBackendLink : IAsyncDisposable
{
    private readonly GateWayOptions options;
    private readonly IBackendConnector connector;

    public GateWayBackendLink(
        CRpcConnection inbound,
        CRpcClient backendClient,
        GateWayOptions options,
        IBackendConnector connector)
    {
        Inbound = inbound ?? throw new ArgumentNullException(nameof(inbound));
        BackendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.connector = connector ?? throw new ArgumentNullException(nameof(connector));
    }

    public CRpcConnection Inbound { get; }

    public CRpcClient BackendClient { get; }

    public async CRpcTask ReconnectAsync()
    {
        await BackendClient.CloseAsync();
        await connector.ConnectAsync(BackendClient, options);
    }

    public async ValueTask DisposeAsync()
    {
        await BackendClient.CloseAsync();
        await BackendClient.ShutdownIoAsync();
    }
}
