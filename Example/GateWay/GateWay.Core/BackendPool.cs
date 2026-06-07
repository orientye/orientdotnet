namespace GateWay;

public sealed class BackendPool
{
    private readonly List<BackendEndpoint> endpoints = new();
    private readonly IBackendPicker picker;
    private int roundRobinCursor;

    public BackendPool(ushort serviceId, IBackendPicker picker)
    {
        ServiceId = serviceId;
        this.picker = picker ?? throw new ArgumentNullException(nameof(picker));
    }

    public ushort ServiceId { get; }

    public IReadOnlyList<BackendEndpoint> Endpoints => endpoints;

    public void AddEndpoint(BackendEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        endpoints.Add(endpoint);
    }

    public BackendEndpoint? Pick()
    {
        return picker.Pick(endpoints, ref roundRobinCursor);
    }

    public void MarkUnhealthy(BackendEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        endpoint.MarkUnhealthy();
    }

    public void MarkHealthy(BackendEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        endpoint.MarkHealthy();
    }
}
