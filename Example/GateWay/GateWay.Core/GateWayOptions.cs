namespace GateWay;

public sealed class GateWayOptions
{
    public string BackendHost { get; init; } = "127.0.0.1";

    public int BackendPort { get; init; } = 7999;

    public int DefaultTimeoutMs { get; init; } = 5000;

    public ushort FallbackServiceId { get; init; } = 0;

    /// <summary>Phase 1: serviceIds that may be forwarded (e.g. HelloWorld Greeter 1000).</summary>
    public HashSet<ushort> RoutedServiceIds { get; init; } = [1000];
}
