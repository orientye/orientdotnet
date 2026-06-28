using Orient.Runtime;
using Orient.Rpc.Client;
using Orient.Rpc.Server;

namespace GateWay;

public sealed class GateWayBackendLink : IAsyncDisposable
{
    private readonly IBackendConnector connector;

    public GateWayBackendLink(
        CRpcConnection inbound,
        CRpcClient backendClient,
        ushort serviceId,
        BackendEndpoint endpoint,
        IBackendConnector connector)
    {
        Inbound = inbound ?? throw new ArgumentNullException(nameof(inbound));
        BackendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
        ServiceId = serviceId;
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        this.connector = connector ?? throw new ArgumentNullException(nameof(connector));
    }

    public CRpcConnection Inbound { get; }

    public CRpcClient BackendClient { get; }

    public ushort ServiceId { get; }

    public BackendEndpoint Endpoint { get; }

    public async OrientTask ReconnectAsync()
    {
        await BackendClient.CloseAsync();
        await connector.ConnectAsync(BackendClient, Endpoint);
    }

    public async ValueTask DisposeAsync()
    {
        await BackendClient.CloseAsync();
        await BackendClient.ShutdownIoAsync();
    }
}
